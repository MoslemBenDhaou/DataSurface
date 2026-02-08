using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.Core.Webhooks;
using DataSurface.EFCore.Caching;
using DataSurface.EFCore.Context;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Exceptions;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Mapper;
using DataSurface.EFCore.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataSurface.EFCore.Services;

/// <summary>
/// Entity Framework Core implementation of <see cref="IDataSurfaceCrudService"/>.
/// </summary>
public sealed class EfDataSurfaceCrudService : IDataSurfaceCrudService
{
    private readonly DbContext _db;
    private readonly IResourceContractProvider _contracts;
    private readonly EfCrudQueryEngine _query;
    private readonly EfCrudMapper _mapper;
    private readonly IServiceProvider _sp;
    private readonly CrudHookDispatcher _hooks;
    private readonly CrudOverrideRegistry _overrides;
    private readonly ILogger<EfDataSurfaceCrudService> _logger;
    private readonly CrudSecurityDispatcher? _security;
    private readonly DataSurfaceMetrics? _metrics;
    private readonly IQueryResultCache? _cache;
    private readonly IWebhookPublisher? _webhooks;

    /// <summary>
    /// Creates a new CRUD service.
    /// </summary>
    /// <param name="db">The EF Core database context.</param>
    /// <param name="contracts">The resource contract provider.</param>
    /// <param name="query">The query engine used for filtering, sorting and paging.</param>
    /// <param name="mapper">The mapper used to apply JSON payloads to entities.</param>
    /// <param name="sp">The service provider.</param>
    /// <param name="hooks">Dispatcher for global and typed hooks.</param>
    /// <param name="overrides">Registry of per-resource override delegates.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="security">Optional security dispatcher for authorization and audit logging.</param>
    /// <param name="metrics">Optional metrics recorder for observability.</param>
    /// <param name="cache">Optional query result cache for read operations.</param>
    /// <param name="webhooks">Optional webhook publisher for CRUD event notifications.</param>
    public EfDataSurfaceCrudService(
        DbContext db,
        IResourceContractProvider contracts,
        EfCrudQueryEngine query,
        EfCrudMapper mapper,
        IServiceProvider sp,
        CrudHookDispatcher hooks,
        CrudOverrideRegistry overrides,
        ILogger<EfDataSurfaceCrudService> logger,
        CrudSecurityDispatcher? security = null,
        DataSurfaceMetrics? metrics = null,
        IQueryResultCache? cache = null,
        IWebhookPublisher? webhooks = null)
    {
        _db = db;
        _contracts = contracts;
        _query = query;
        _mapper = mapper;
        _sp = sp;
        _hooks = hooks;
        _overrides = overrides;
        _logger = logger;
        _security = security;
        _metrics = metrics;
        _cache = cache;
        _webhooks = webhooks;
    }

    /// <inheritdoc />
    /// <summary>
    /// Retrieves a list of resources.
    /// </summary>
    /// <param name="resourceKey">The key of the resource to retrieve.</param>
    /// <param name="spec">The query specification.</param>
    /// <param name="expand">The expand specification.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A paged result of JSON objects.</returns>
    public async Task<PagedResult<JsonObject>> ListAsync(
        string resourceKey, QuerySpec spec, ExpandSpec? expand = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.List);
        DataSurfaceTracing.AddQueryParameters(activity, spec.Page, spec.PageSize, spec.Filters?.Count ?? 0, string.IsNullOrEmpty(spec.Sort) ? 0 : spec.Sort.Split(',').Length);
        DataSurfaceTracing.AddExpandInfo(activity, expand?.Expand);

        _logger.LogDebug("List {Resource} page={Page} pageSize={PageSize}", resourceKey, spec.Page, spec.PageSize);

        var c = _contracts.GetByResourceKey(resourceKey);
        EnsureEnabled(c, CrudOperation.List);

        // Check cache only when no per-user security features are active (to avoid serving cached data across users)
        var useCache = _cache is not null && !HasPerUserSecurity(c);
        string? cacheKey = null;
        if (useCache)
        {
            cacheKey = _cache!.GenerateListCacheKey(resourceKey, spec, expand);
            var cached = await _cache.GetListAsync(resourceKey, cacheKey, ct);
            if (cached is not null)
            {
                sw.Stop();
                DataSurfaceTracing.RecordSuccess(activity, cached.Items.Count);
                _metrics?.RecordOperation(resourceKey, CrudOperation.List, sw.Elapsed.TotalMilliseconds, cached.Items.Count);
                _logger.LogDebug("List {Resource} cache hit, returned {Count}/{Total} items", resourceKey, cached.Items.Count, cached.Total);
                return cached;
            }
        }

        var hookCtx = NewHookCtx(c, CrudOperation.List);
        var svcCtx = NewSvcCtx();

        await _hooks.BeforeGlobalAsync(hookCtx);

        if (_overrides.TryGet<ListOverride>(c.ResourceKey, CrudOperation.List, out var ov))
        {
            var result = await ov!(c, spec, expand, svcCtx, ct);
            await _hooks.AfterGlobalAsync(hookCtx);
            sw.Stop();
            DataSurfaceTracing.RecordSuccess(activity, result.Items.Count);
            _metrics?.RecordOperation(resourceKey, CrudOperation.List, sw.Elapsed.TotalMilliseconds, result.Items.Count);
            return result;
        }

        var (clrType, set) = ResolveSet(c);

        // Apply row-level security filter
        var filteredSet = _security is not null 
            ? _security.ApplyResourceFilter(set, clrType, c) 
            : set;

        // Apply tenant isolation filter
        if (_security is not null && c.Tenant is not null)
            filteredSet = _security.ApplyTenantFilter(filteredSet, clrType, c);

        var baseQuery = ApplyExpand(filteredSet, c, expand);
        var filtered = ApplyFilterSpec(baseQuery, clrType, c, spec);
        var shaped = ApplyQuerySpec(baseQuery, clrType, c, spec);

        var total = await CountAsync(filtered, ct);
        var pageItems = await ToListAsync(shaped, ct);

        // optional: after-read hook per item (expensive; still useful)
        foreach (var e in pageItems)
            await InvokeTypedAfterRead(e, hookCtx);

        var json = pageItems.Select(e => EntityToJson(e, c, expand, spec.Fields)).ToList();

        // Apply field-level authorization (redact unauthorized fields)
        _security?.RedactUnauthorizedFields(c, json);

        await _hooks.AfterGlobalAsync(hookCtx);

        // Audit logging for list operation
        if (_security is not null)
            await _security.LogAuditAsync(_security.CreateAuditEntry(CrudOperation.List, resourceKey), ct);

        sw.Stop();
        DataSurfaceTracing.RecordSuccess(activity, json.Count);
        _metrics?.RecordOperation(resourceKey, CrudOperation.List, sw.Elapsed.TotalMilliseconds, json.Count);

        _logger.LogDebug("List {Resource} completed in {ElapsedMs}ms, returned {Count}/{Total} items",
            resourceKey, sw.ElapsedMilliseconds, json.Count, total);

        var pagedResult = new PagedResult<JsonObject>(
            json,
            Math.Max(1, spec.Page),
            Math.Clamp(spec.PageSize, 1, c.Query.MaxPageSize),
            total);

        // Store in cache (only when security is not active)
        if (useCache && cacheKey is not null)
            await _cache!.SetListAsync(resourceKey, cacheKey, pagedResult, duration: null, ct);

        return pagedResult;
    }

    /// <inheritdoc />
    /// <summary>
    /// Retrieves a single resource by ID.
    /// </summary>
    /// <param name="resourceKey">The key of the resource to retrieve.</param>
    /// <param name="id">The ID of the resource to retrieve.</param>
    /// <param name="expand">The expand specification.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A JSON object or null if not found.</returns>
    public async Task<JsonObject?> GetAsync(
        string resourceKey, object id, ExpandSpec? expand = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.Get, id);
        DataSurfaceTracing.AddExpandInfo(activity, expand?.Expand);

        _logger.LogDebug("Get {Resource} id={Id}", resourceKey, id);

        var c = _contracts.GetByResourceKey(resourceKey);
        EnsureEnabled(c, CrudOperation.Get);

        // Check cache only when no per-user security features are active (to avoid serving cached data across users)
        var useCache = _cache is not null && !HasPerUserSecurity(c);
        if (useCache)
        {
            var cached = await _cache!.GetAsync(resourceKey, id, ct);
            if (cached is not null)
            {
                sw.Stop();
                DataSurfaceTracing.RecordSuccess(activity);
                _metrics?.RecordOperation(resourceKey, CrudOperation.Get, sw.Elapsed.TotalMilliseconds);
                _logger.LogDebug("Get {Resource} id={Id} cache hit", resourceKey, id);
                return cached;
            }
        }

        var hookCtx = NewHookCtx(c, CrudOperation.Get);
        var svcCtx = NewSvcCtx();

        await _hooks.BeforeGlobalAsync(hookCtx);

        if (_overrides.TryGet<GetOverride>(c.ResourceKey, CrudOperation.Get, out var ov))
        {
            var result = await ov!(c, id, expand, svcCtx, ct);
            await _hooks.AfterGlobalAsync(hookCtx);
            sw.Stop();
            DataSurfaceTracing.RecordSuccess(activity, result is null ? 0 : 1);
            _metrics?.RecordOperation(resourceKey, CrudOperation.Get, sw.Elapsed.TotalMilliseconds, result is null ? 0 : 1);
            return result;
        }

        var (clrType, set) = ResolveSet(c);

        // Apply row-level security filter
        var filteredSet = _security is not null 
            ? _security.ApplyResourceFilter(set, clrType, c) 
            : set;

        // Apply tenant isolation filter
        if (_security is not null && c.Tenant is not null)
            filteredSet = _security.ApplyTenantFilter(filteredSet, clrType, c);

        var q = ApplyExpand(filteredSet, c, expand);

        var entity = await FindByIdAsync(q, clrType, c, id, ct);
        if (entity is null)
        {
            await _hooks.AfterGlobalAsync(hookCtx);
            sw.Stop();
            _metrics?.RecordOperation(resourceKey, CrudOperation.Get, sw.Elapsed.TotalMilliseconds, 0);
            return null;
        }

        // Resource-level authorization check
        if (_security is not null)
            await _security.AuthorizeResourceAsync(c, entity, clrType, CrudOperation.Get, ct);

        await InvokeTypedAfterRead(entity, hookCtx);

        var json = EntityToJson(entity, c, expand);

        // Apply field-level authorization (redact unauthorized fields)
        _security?.RedactUnauthorizedFields(c, json);

        await _hooks.AfterGlobalAsync(hookCtx);

        // Audit logging
        if (_security is not null)
            await _security.LogAuditAsync(_security.CreateAuditEntry(CrudOperation.Get, resourceKey, id.ToString()), ct);

        sw.Stop();
        DataSurfaceTracing.RecordSuccess(activity);
        _metrics?.RecordOperation(resourceKey, CrudOperation.Get, sw.Elapsed.TotalMilliseconds);

        _logger.LogDebug("Get {Resource} id={Id} completed in {ElapsedMs}ms", resourceKey, id, sw.ElapsedMilliseconds);

        // Store in cache (only when security is not active)
        if (useCache)
            await _cache!.SetAsync(resourceKey, id, json, duration: null, ct);

        return json;
    }

    /// <inheritdoc />
    /// <summary>
    /// Creates a new resource.
    /// </summary>
    /// <param name="resourceKey">The key of the resource to create.</param>
    /// <param name="body">The JSON payload to create the resource with.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A JSON object representing the created resource.</returns>
    public async Task<JsonObject> CreateAsync(string resourceKey, JsonObject body, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.Create);

        _logger.LogDebug("Create {Resource}", resourceKey);

        var c = _contracts.GetByResourceKey(resourceKey);
        EnsureEnabled(c, CrudOperation.Create);

        ValidateBody(c, CrudOperation.Create, body);

        // Validate field-level write authorization
        _security?.ValidateFieldWriteAuthorization(c, body, CrudOperation.Create);

        var hookCtx = NewHookCtx(c, CrudOperation.Create);
        var svcCtx = NewSvcCtx();

        await _hooks.BeforeGlobalAsync(hookCtx);

        if (_overrides.TryGet<CreateOverride>(c.ResourceKey, CrudOperation.Create, out var ov))
        {
            var result = await ov!(c, body, svcCtx, ct);
            await _hooks.AfterGlobalAsync(hookCtx);
            sw.Stop();
            DataSurfaceTracing.RecordSuccess(activity);
            _metrics?.RecordOperation(resourceKey, CrudOperation.Create, sw.Elapsed.TotalMilliseconds);
            return result;
        }

        var (clrType, _) = ResolveSet(c);
        var entity = CreateEntityByType(clrType, body, c);

        // Set tenant value on new entity
        if (_security is not null && c.Tenant is not null)
            _security.SetTenantValue(entity, c);

        await InvokeTypedBeforeCreate(entity, body, hookCtx);

        _db.Add(entity);
        await _db.SaveChangesAsync(ct);

        await InvokeTypedAfterCreate(entity, hookCtx);

        var json = EntityToJson(entity, c, expand: null);

        await _hooks.AfterGlobalAsync(hookCtx);

        // Audit logging
        if (_security is not null)
        {
            var keyVal = GetEntityKeyValue(entity, c);
            await _security.LogAuditAsync(_security.CreateAuditEntry(
                CrudOperation.Create, resourceKey, keyVal?.ToString(), changes: body), ct);
        }

        sw.Stop();
        DataSurfaceTracing.RecordSuccess(activity);
        _metrics?.RecordOperation(resourceKey, CrudOperation.Create, sw.Elapsed.TotalMilliseconds);

        // Invalidate list cache (new item affects list results)
        if (_cache is not null)
            await _cache.InvalidateResourceAsync(resourceKey, ct);

        // Publish webhook event
        await PublishWebhookAsync(resourceKey, CrudOperation.Create, GetEntityKeyValue(entity, c)?.ToString(), json, ct);

        _logger.LogInformation("Created {Resource} in {ElapsedMs}ms", resourceKey, sw.ElapsedMilliseconds);
        return json;
    }

    /// <inheritdoc />
    /// <summary>
    /// Updates an existing resource.
    /// </summary>
    /// <param name="resourceKey">The key of the resource to update.</param>
    /// <param name="id">The ID of the resource to update.</param>
    /// <param name="patch">The JSON payload to update the resource with.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A JSON object representing the updated resource.</returns>
    public async Task<JsonObject> UpdateAsync(string resourceKey, object id, JsonObject patch, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.Update, id);

        _logger.LogDebug("Update {Resource} id={Id}", resourceKey, id);

        var c = _contracts.GetByResourceKey(resourceKey);
        EnsureEnabled(c, CrudOperation.Update);
        ValidateBody(c, CrudOperation.Update, patch);

        // Validate field-level write authorization
        _security?.ValidateFieldWriteAuthorization(c, patch, CrudOperation.Update);

        var hookCtx = NewHookCtx(c, CrudOperation.Update);
        var svcCtx = NewSvcCtx();

        await _hooks.BeforeGlobalAsync(hookCtx);

        if (_overrides.TryGet<UpdateOverride>(c.ResourceKey, CrudOperation.Update, out var ov))
        {
            var result = await ov!(c, id, patch, svcCtx, ct);
            await _hooks.AfterGlobalAsync(hookCtx);
            sw.Stop();
            DataSurfaceTracing.RecordSuccess(activity);
            _metrics?.RecordOperation(resourceKey, CrudOperation.Update, sw.Elapsed.TotalMilliseconds);
            return result;
        }

        var (clrType, set) = ResolveSet(c);

        // Apply row-level security filter
        var filteredSet = _security is not null 
            ? _security.ApplyResourceFilter(set, clrType, c) 
            : set;

        // Apply tenant isolation filter
        if (_security is not null && c.Tenant is not null)
            filteredSet = _security.ApplyTenantFilter(filteredSet, clrType, c);

        var entity = await FindByIdAsync(filteredSet, clrType, c, id, ct) ?? throw new CrudNotFoundException(resourceKey, id);

        // Resource-level authorization check
        if (_security is not null)
            await _security.AuthorizeResourceAsync(c, entity, clrType, CrudOperation.Update, ct);

        // Capture previous values for audit
        var previousValues = _security is not null ? EntityToJson(entity, c, expand: null) : null;

        await InvokeTypedBeforeUpdate(entity, patch, hookCtx);

        InvokeTypedApplyUpdate(entity, patch, c);
        await _db.SaveChangesAsync(ct);

        await InvokeTypedAfterUpdate(entity, hookCtx);

        var json = EntityToJson(entity, c, expand: null);

        await _hooks.AfterGlobalAsync(hookCtx);

        // Audit logging
        if (_security is not null)
            await _security.LogAuditAsync(_security.CreateAuditEntry(
                CrudOperation.Update, resourceKey, id.ToString(), changes: patch, previousValues: previousValues), ct);

        sw.Stop();
        DataSurfaceTracing.RecordSuccess(activity);
        _metrics?.RecordOperation(resourceKey, CrudOperation.Update, sw.Elapsed.TotalMilliseconds);

        // Invalidate cache (updated item and list results are stale)
        if (_cache is not null)
        {
            await _cache.InvalidateAsync(resourceKey, id, ct);
            await _cache.InvalidateResourceAsync(resourceKey, ct);
        }

        // Publish webhook event
        await PublishWebhookAsync(resourceKey, CrudOperation.Update, id.ToString(), json, ct);

        _logger.LogInformation("Updated {Resource} id={Id} in {ElapsedMs}ms", resourceKey, id, sw.ElapsedMilliseconds);
        return json;
    }

    /// <inheritdoc />
    /// <summary>
    /// Deletes a resource.
    /// </summary>
    /// <param name="resourceKey">The key of the resource to delete.</param>
    /// <param name="id">The ID of the resource to delete.</param>
    /// <param name="deleteSpec">The delete specification.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task DeleteAsync(string resourceKey, object id, CrudDeleteSpec? deleteSpec = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.Delete, id);
        activity?.SetTag("datasurface.hard_delete", deleteSpec?.HardDelete ?? false);

        _logger.LogDebug("Delete {Resource} id={Id} hard={Hard}", resourceKey, id, deleteSpec?.HardDelete ?? false);

        var c = _contracts.GetByResourceKey(resourceKey);
        EnsureEnabled(c, CrudOperation.Delete);

        var hookCtx = NewHookCtx(c, CrudOperation.Delete);
        var svcCtx = NewSvcCtx();

        await _hooks.BeforeGlobalAsync(hookCtx);

        if (_overrides.TryGet<DeleteOverride>(c.ResourceKey, CrudOperation.Delete, out var ov))
        {
            await ov!(c, id, deleteSpec, svcCtx, ct);
            await _hooks.AfterGlobalAsync(hookCtx);
            sw.Stop();
            DataSurfaceTracing.RecordSuccess(activity);
            _metrics?.RecordOperation(resourceKey, CrudOperation.Delete, sw.Elapsed.TotalMilliseconds);
            return;
        }

        var (clrType, set) = ResolveSet(c);

        // Apply row-level security filter
        var filteredSet = _security is not null 
            ? _security.ApplyResourceFilter(set, clrType, c) 
            : set;

        // Apply tenant isolation filter
        if (_security is not null && c.Tenant is not null)
            filteredSet = _security.ApplyTenantFilter(filteredSet, clrType, c);

        var entity = await FindByIdAsync(filteredSet, clrType, c, id, ct) ?? throw new CrudNotFoundException(resourceKey, id);

        // Resource-level authorization check
        if (_security is not null)
            await _security.AuthorizeResourceAsync(c, entity, clrType, CrudOperation.Delete, ct);

        // Concurrency check: verify If-Match token matches entity's current concurrency value
        if (!string.IsNullOrWhiteSpace(deleteSpec?.ConcurrencyToken))
        {
            var cc = c.Operations.TryGetValue(CrudOperation.Update, out var oc) ? oc.Concurrency : null;
            if (cc is not null && cc.Mode == ConcurrencyMode.RowVersion)
            {
                // Find the CLR property name from the field's ApiName
                var field = c.Fields.FirstOrDefault(f => f.ApiName.Equals(cc.FieldApiName, StringComparison.OrdinalIgnoreCase));
                var prop = field is not null ? clrType.GetProperty(field.Name) : null;
                if (prop is not null)
                {
                    var currentValue = prop.GetValue(entity);
                    var currentToken = currentValue switch
                    {
                        byte[] bytes => Convert.ToBase64String(bytes),
                        _ => currentValue?.ToString()
                    };
                    if (currentToken != deleteSpec.ConcurrencyToken)
                        throw new CrudConcurrencyException(resourceKey, id, "Entity has been modified since it was retrieved.");
                }
            }
        }

        await InvokeTypedBeforeDelete(entity, hookCtx);

        var hard = deleteSpec?.HardDelete ?? false;

        if (!hard && entity is ISoftDelete sd)
        {
            sd.IsDeleted = true;
            await _db.SaveChangesAsync(ct);

            await InvokeTypedAfterDelete(entity, hookCtx);
            await _hooks.AfterGlobalAsync(hookCtx);

            // Audit logging
            if (_security is not null)
                await _security.LogAuditAsync(_security.CreateAuditEntry(CrudOperation.Delete, resourceKey, id.ToString()), ct);

            sw.Stop();
            DataSurfaceTracing.RecordSuccess(activity);
            _metrics?.RecordOperation(resourceKey, CrudOperation.Delete, sw.Elapsed.TotalMilliseconds);

            // Invalidate cache (deleted item and list results are stale)
            if (_cache is not null)
            {
                await _cache.InvalidateAsync(resourceKey, id, ct);
                await _cache.InvalidateResourceAsync(resourceKey, ct);
            }

            // Publish webhook event
            await PublishWebhookAsync(resourceKey, CrudOperation.Delete, id.ToString(), null, ct);

            _logger.LogInformation("Soft-deleted {Resource} id={Id} in {ElapsedMs}ms", resourceKey, id, sw.ElapsedMilliseconds);
            return;
        }

        _db.Remove(entity);
        await _db.SaveChangesAsync(ct);

        await InvokeTypedAfterDelete(entity, hookCtx);
        await _hooks.AfterGlobalAsync(hookCtx);

        // Audit logging
        if (_security is not null)
            await _security.LogAuditAsync(_security.CreateAuditEntry(CrudOperation.Delete, resourceKey, id.ToString()), ct);

        sw.Stop();
        DataSurfaceTracing.RecordSuccess(activity);
        _metrics?.RecordOperation(resourceKey, CrudOperation.Delete, sw.Elapsed.TotalMilliseconds);

        // Invalidate cache (deleted item and list results are stale)
        if (_cache is not null)
        {
            await _cache.InvalidateAsync(resourceKey, id, ct);
            await _cache.InvalidateResourceAsync(resourceKey, ct);
        }

        // Publish webhook event
        await PublishWebhookAsync(resourceKey, CrudOperation.Delete, id.ToString(), null, ct);

        _logger.LogInformation("Deleted {Resource} id={Id} in {ElapsedMs}ms", resourceKey, id, sw.ElapsedMilliseconds);
    }

    // ----------------- helpers -----------------

    private static void EnsureEnabled(ResourceContract c, CrudOperation op)
    {
        if (!c.Operations.TryGetValue(op, out var oc) || !oc.Enabled)
            throw new InvalidOperationException($"Operation '{op}' is disabled for resource '{c.ResourceKey}'.");
    }

    private static object? GetEntityKeyValue(object entity, ResourceContract c)
    {
        var prop = entity.GetType().GetProperty(c.Key.Name);
        return prop?.GetValue(entity);
    }
    
    private Task InvokeTypedAfterRead(object entity, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.AfterReadAsync))!
            .MakeGenericMethod(entity.GetType());
        return (Task)m.Invoke(_hooks, new object[] { entity, ctx })!;
    }

    private Task InvokeTypedBeforeCreate(object entity, JsonObject body, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.BeforeCreateAsync))!
            .MakeGenericMethod(entity.GetType());
        return (Task)m.Invoke(_hooks, new object[] { entity, body, ctx })!;
    }

    private Task InvokeTypedAfterCreate(object entity, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.AfterCreateAsync))!
            .MakeGenericMethod(entity.GetType());
        return (Task)m.Invoke(_hooks, new object[] { entity, ctx })!;
    }

    private Task InvokeTypedBeforeUpdate(object entity, JsonObject patch, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.BeforeUpdateAsync))!
            .MakeGenericMethod(entity.GetType());
        return (Task)m.Invoke(_hooks, new object[] { entity, patch, ctx })!;
    }

    private Task InvokeTypedAfterUpdate(object entity, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.AfterUpdateAsync))!
            .MakeGenericMethod(entity.GetType());
        return (Task)m.Invoke(_hooks, new object[] { entity, ctx })!;
    }

    private void InvokeTypedApplyUpdate(object entity, JsonObject patch, ResourceContract c)
    {
        var m = typeof(EfCrudMapper).GetMethod(nameof(EfCrudMapper.ApplyUpdate))!
            .MakeGenericMethod(entity.GetType());
        m.Invoke(_mapper, new object[] { entity, patch, c, _db });
    }

    private Task InvokeTypedBeforeDelete(object entity, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.BeforeDeleteAsync))!
            .MakeGenericMethod(entity.GetType());
        return (Task)m.Invoke(_hooks, new object[] { entity, ctx })!;
    }

    private Task InvokeTypedAfterDelete(object entity, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.AfterDeleteAsync))!
            .MakeGenericMethod(entity.GetType());
        return (Task)m.Invoke(_hooks, new object[] { entity, ctx })!;
    }

    private CrudHookContext NewHookCtx(ResourceContract c, CrudOperation op)
        => new()
        {
            Operation = op,
            Contract = c,
            Db = _db,
            Services = _sp
        };

    private bool HasPerUserSecurity(ResourceContract c)
    {
        if (c.Tenant is not null) return true;
        if (_security is null) return false;
        if (_sp.GetService(typeof(IFieldAuthorizer)) is not null) return true;
        if (((IEnumerable<IResourceFilter>)_sp.GetService(typeof(IEnumerable<IResourceFilter>))!).Any()) return true;
        return false;
    }

    private CrudServiceContext NewSvcCtx()
        => new()
        {
            Services = _sp,
            Db = _db,
            Mapper = _mapper,
            Query = _query,
            Contracts = _contracts
        };

    // Static cache for CLR type resolution to avoid scanning assemblies on every request
    private static readonly ConcurrentDictionary<string, Type> _typeCache = new(StringComparer.OrdinalIgnoreCase);

    private (Type clrType, IQueryable set) ResolveSet(ResourceContract c)
    {
        var clrType = _typeCache.GetOrAdd(c.ResourceKey, key =>
            AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => SafeGetTypes(a))
                .FirstOrDefault(t => t.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Cannot resolve CLR type for resourceKey '{key}'."));

        var set = (IQueryable)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
            .MakeGenericMethod(clrType)
            .Invoke(_db, null)!;

        return (clrType, set);
    }

    private static IEnumerable<Type> SafeGetTypes(System.Reflection.Assembly a)
    {
        try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
    }

    private IQueryable ApplyExpand(IQueryable query, ResourceContract c, ExpandSpec? expand)
    {
        // Merge default expanded relations with explicitly requested expansions
        var toExpand = new HashSet<string>(c.Read.DefaultExpand, StringComparer.OrdinalIgnoreCase);
        if (expand is not null)
        {
            foreach (var e in expand.Expand)
                toExpand.Add(e);
        }

        if (toExpand.Count == 0) return query;

        var allowed = new HashSet<string>(c.Read.ExpandAllowed, StringComparer.OrdinalIgnoreCase);
        foreach (var apiName in toExpand)
        {
            if (!allowed.Contains(apiName)) continue;

            // Find the relation to get the CLR property name
            var rel = c.Relations.FirstOrDefault(r => r.ApiName.Equals(apiName, StringComparison.OrdinalIgnoreCase));
            if (rel is null) continue;

            // use string-based Include with CLR property name (not API name)
            query = (IQueryable)typeof(EntityFrameworkQueryableExtensions)
                .GetMethods()
                .Single(m => m.Name == "Include"
                             && m.IsGenericMethodDefinition
                             && m.GetGenericArguments().Length == 1
                             && m.GetParameters().Length == 2
                             && m.GetParameters()[1].ParameterType == typeof(string))
                .MakeGenericMethod(query.ElementType)
                .Invoke(null, new object?[] { query, rel.Name })!;

        }

        return query;
    }

    private IQueryable ApplyQuerySpec(IQueryable query, Type clrType, ResourceContract c, QuerySpec spec)
    {
        // Use EfCrudQueryEngine only for generic TEntity; here we’re Type-based.
        // Minimal: call engine via reflection.
        var m = typeof(EfCrudQueryEngine).GetMethod(nameof(EfCrudQueryEngine.Apply))!
            .MakeGenericMethod(clrType);
        return (IQueryable)m.Invoke(_query, new object[] { query, c, spec })!;
    }

    private IQueryable ApplyFilterSpec(IQueryable query, Type clrType, ResourceContract c, QuerySpec spec)
    {
        var m = typeof(EfCrudQueryEngine).GetMethod(nameof(EfCrudQueryEngine.ApplyFiltersAndSort))!
            .MakeGenericMethod(clrType);
        return (IQueryable)m.Invoke(_query, new object[] { query, c, spec })!;
    }

    private async Task<int> CountAsync(IQueryable query, CancellationToken ct)
    {
        var m = typeof(EntityFrameworkQueryableExtensions).GetMethods()
            .First(x => x.Name == nameof(EntityFrameworkQueryableExtensions.CountAsync) && x.GetParameters().Length == 2);
        var gm = m.MakeGenericMethod(query.ElementType);
        var t = (Task<int>)gm.Invoke(null, new object[] { query, ct })!;
        return await t;
    }

    private async Task<List<object>> ToListAsync(IQueryable query, CancellationToken ct)
    {
        var m = typeof(EntityFrameworkQueryableExtensions).GetMethods()
            .First(x => x.Name == nameof(EntityFrameworkQueryableExtensions.ToListAsync) && x.GetParameters().Length == 2);
        var gm = m.MakeGenericMethod(query.ElementType);
        var t = (Task)gm.Invoke(null, new object[] { query, ct })!;
        await t.ConfigureAwait(false);

        // Task<List<T>> returned; extract via reflection
        var resultProp = t.GetType().GetProperty("Result")!;
        var list = (System.Collections.IEnumerable)resultProp.GetValue(t)!;
        return list.Cast<object>().ToList();
    }

    private async Task<object?> FindByIdAsync(IQueryable query, Type clrType, ResourceContract c, object id, CancellationToken ct)
    {
        var keyClrName = c.Key.Name;
        var prop = clrType.GetProperty(keyClrName) ?? throw new InvalidOperationException($"Key '{keyClrName}' not found.");

        var param = System.Linq.Expressions.Expression.Parameter(clrType, "e");
        var member = System.Linq.Expressions.Expression.Property(param, prop);
        var constant = System.Linq.Expressions.Expression.Constant(Convert.ChangeType(id, prop.PropertyType), prop.PropertyType);
        var eq = System.Linq.Expressions.Expression.Equal(member, constant);
        var lambda = System.Linq.Expressions.Expression.Lambda(eq, param);

        var where = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.Where) && m.GetParameters().Length == 2)
            .MakeGenericMethod(clrType);

        var filtered = (IQueryable)where.Invoke(null, new object[] { query, lambda })!;

        var firstAsync = typeof(EntityFrameworkQueryableExtensions).GetMethods()
            .First(m => m.Name == nameof(EntityFrameworkQueryableExtensions.FirstOrDefaultAsync) && m.GetParameters().Length == 2)
            .MakeGenericMethod(clrType);

        var task = (Task)firstAsync.Invoke(null, new object[] { filtered, ct })!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private object CreateEntityByType(Type clrType, JsonObject body, ResourceContract c)
    {
        // Use mapper to create and populate entity
        var m = typeof(EfCrudMapper).GetMethod(nameof(EfCrudMapper.CreateEntity))!
            .MakeGenericMethod(clrType);
        return m.Invoke(_mapper, new object[] { body, c, _db })!;
    }

    private static JsonObject EntityToJson(object entity, ResourceContract c, ExpandSpec? expand, string? projectedFields = null)
    {
        var o = new JsonObject();
        var readFields = c.Fields.Where(f => f.InRead && !f.Hidden).ToList();

        // Apply field projection if specified
        if (!string.IsNullOrWhiteSpace(projectedFields))
        {
            var requested = new HashSet<string>(
                projectedFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
            readFields = readFields.Where(f => requested.Contains(f.ApiName)).ToList();
        }

        foreach (var f in readFields)
        {
            // Handle computed fields
            if (f.Computed && !string.IsNullOrWhiteSpace(f.ComputedExpression))
            {
                var computedVal = EvaluateComputedExpression(entity, f.ComputedExpression);
                o[f.ApiName] = computedVal is null ? null : JsonValue.Create(computedVal);
                continue;
            }

            var p = entity.GetType().GetProperty(f.Name);
            if (p == null) continue;

            var val = p.GetValue(entity);
            o[f.ApiName] = val is null ? null : JsonValue.Create(val);
        }

        // expand: include nav objects as nested JSON (depth 1)
        // Merge default expanded relations with explicitly requested expansions
        var toSerialize = new HashSet<string>(c.Read.DefaultExpand, StringComparer.OrdinalIgnoreCase);
        if (expand is not null)
        {
            foreach (var e in expand.Expand)
                toSerialize.Add(e);
        }

        if (toSerialize.Count > 0)
        {
            // Derive camelCase convention from the contract: if any field has a PascalCase
            // CLR name but a camelCase API name, the convention is camelCase.
            var useCamelCase = c.Fields.Any(f =>
                f.Name.Length > 0 && f.ApiName.Length > 0 &&
                char.IsUpper(f.Name[0]) && char.IsLower(f.ApiName[0]));

            var allowed = new HashSet<string>(c.Read.ExpandAllowed, StringComparer.OrdinalIgnoreCase);
            foreach (var relApi in toSerialize.Where(x => allowed.Contains(x)))
            {
                var rel = c.Relations.FirstOrDefault(r => r.ApiName.Equals(relApi, StringComparison.OrdinalIgnoreCase));
                if (rel == null) continue;

                var navProp = entity.GetType().GetProperty(rel.Name);
                if (navProp == null) continue;

                var nav = navProp.GetValue(entity);
                if (nav is null) { o[relApi] = null; continue; }

                if (nav is System.Collections.IEnumerable seq && nav is not string)
                {
                    var arr = new JsonArray();
                    foreach (var item in seq.Cast<object>())
                        arr.Add(SimpleObjectToJson(item, useCamelCase));
                    o[relApi] = arr;
                }
                else
                {
                    o[relApi] = SimpleObjectToJson(nav, useCamelCase);
                }
            }
        }

        return o;
    }

    private static JsonObject SimpleObjectToJson(object obj, bool useCamelCase)
    {
        var j = new JsonObject();
        var t = obj.GetType();

        // minimal: include scalar public props (Id + common scalars)
        foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (!p.CanRead) continue;
            if (p.GetIndexParameters().Length > 0) continue;

            var pt = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            var isScalar =
                pt.IsEnum ||
                pt == typeof(string) || pt == typeof(int) || pt == typeof(long) || pt == typeof(decimal) ||
                pt == typeof(bool) || pt == typeof(DateTime) || pt == typeof(Guid);

            if (!isScalar) continue;

            var key = useCamelCase
                ? char.ToLowerInvariant(p.Name[0]) + p.Name[1..]
                : p.Name;
            j[key] = JsonValue.Create(p.GetValue(obj));
        }
        return j;
    }

    private static object? EvaluateComputedExpression(object entity, string expression)
    {
        // Simple expression evaluator supporting property concatenation and numeric summation
        // Format: "PropertyA + ' ' + PropertyB" or "Salary + Bonus"
        try
        {
            var entityType = entity.GetType();
            var parts = expression.Split(new[] { " + " }, StringSplitOptions.None);

            // Determine if all non-literal parts are numeric properties
            var allNumeric = true;
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith("'") && trimmed.EndsWith("'")) { allNumeric = false; break; }
                var prop = entityType.GetProperty(trimmed);
                if (prop is null) continue;
                var pt = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (pt != typeof(int) && pt != typeof(long) && pt != typeof(decimal)
                    && pt != typeof(double) && pt != typeof(float))
                {
                    allNumeric = false;
                    break;
                }
            }

            if (allNumeric && parts.Length > 0)
            {
                // Numeric summation
                decimal sum = 0;
                foreach (var part in parts)
                {
                    var prop = entityType.GetProperty(part.Trim());
                    if (prop is null) continue;
                    var val = prop.GetValue(entity);
                    if (val is not null)
                        sum += Convert.ToDecimal(val, System.Globalization.CultureInfo.InvariantCulture);
                }
                return sum;
            }

            // String concatenation
            var result = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith("'") && trimmed.EndsWith("'"))
                {
                    result.Append(trimmed[1..^1]);
                    continue;
                }
                var prop = entityType.GetProperty(trimmed);
                if (prop is not null)
                {
                    var val = prop.GetValue(entity);
                    result.Append(val?.ToString() ?? "");
                }
            }
            return result.ToString();
        }
        catch
        {
            return null;
        }
    }

    private async Task PublishWebhookAsync(
        string resourceKey,
        CrudOperation operation,
        string? entityId,
        JsonObject? payload,
        CancellationToken ct)
    {
        if (_webhooks is null) return;

        try
        {
            var webhookEvent = new WebhookEvent(
                resourceKey,
                operation,
                entityId,
                payload,
                DateTime.UtcNow);

            await _webhooks.PublishAsync(webhookEvent, ct);
        }
        catch (Exception ex)
        {
            // Don't fail the operation if webhook publishing fails
            _logger.LogWarning(ex, "Failed to publish webhook for {Operation} on {Resource}", operation, resourceKey);
        }
    }

    private static void ValidateBody(ResourceContract c, CrudOperation op, JsonObject body)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var oc = c.Operations[op];

        var allowed = new HashSet<string>(oc.InputShape, StringComparer.OrdinalIgnoreCase);

        // unknown fields
        foreach (var key in body.Select(kv => kv.Key))
        {
            if (!allowed.Contains(key))
                errors[key] = new[] { "Field is not allowed for this operation." };
        }

        // required on create
        if (op == CrudOperation.Create)
        {
            foreach (var req in oc.RequiredOnCreate)
            {
                if (!body.ContainsKey(req))
                    errors[req] = new[] { "Field is required." };
            }
        }

        // immutable on update (skip concurrency field — it's validated separately)
        if (op == CrudOperation.Update)
        {
            var concurrencyApiName = oc.Concurrency?.FieldApiName;
            foreach (var imm in oc.ImmutableFields)
            {
                if (body.ContainsKey(imm)
                    && !string.Equals(imm, concurrencyApiName, StringComparison.OrdinalIgnoreCase))
                    errors[imm] = new[] { "Field is immutable." };
            }

            // concurrency required
            if (oc.Concurrency is { RequiredOnUpdate: true } cc)
            {
                if (!body.ContainsKey(cc.FieldApiName))
                    errors[cc.FieldApiName] = new[] { "Concurrency token is required." };
            }
        }

        // Field-level validation (MinLength, MaxLength, Min, Max, Regex, AllowedValues)
        Validation.FieldValidator.ValidateFieldConstraints(c, body, errors);

        if (errors.Count > 0)
            throw new CrudRequestValidationException(errors);
    }
}
