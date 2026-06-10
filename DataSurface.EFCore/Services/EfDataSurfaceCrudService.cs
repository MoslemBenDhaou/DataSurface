using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using DataSurface.Core;
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
using DataSurface.EFCore.Queries;
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
    private readonly CompiledQueryCache? _compiledQueries;
    private readonly DataSurfaceFeatures _features;

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
    /// <param name="compiledQueries">Optional compiled-query cache used to speed up simple by-id reads.</param>
    /// <param name="features">Optional feature flags; a feature runs only when its flag is enabled and its wiring is present.</param>
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
        IWebhookPublisher? webhooks = null,
        CompiledQueryCache? compiledQueries = null,
        DataSurfaceFeatures? features = null)
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
        _features = features ?? new DataSurfaceFeatures();
        // Feature flags are an AND-gate: these capabilities run only when the flag is on AND the dependency
        // is registered. Null the dependency out when its flag is off, so the existing null-checks skip it.
        _metrics = _features.EnableMetrics ? metrics : null;
        _cache = _features.EnableQueryCaching ? cache : null;
        _webhooks = _features.EnableWebhooks ? webhooks : null;
        _compiledQueries = compiledQueries;
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
        using var activity = _features.EnableTracing ? DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.List) : null;
        DataSurfaceTracing.AddQueryParameters(activity, spec.Page, spec.PageSize, spec.Filters?.Count ?? 0, string.IsNullOrEmpty(spec.Sort) ? 0 : spec.Sort.Split(',').Length);
        DataSurfaceTracing.AddExpandInfo(activity, expand?.Expand);

        _logger.LogDebug("List {Resource} page={Page} pageSize={PageSize}", resourceKey, spec.Page, spec.PageSize);

        var c = _contracts.GetByResourceKey(resourceKey);
        EnsureEnabled(c, CrudOperation.List);

        var hookCtx = NewHookCtx(c, CrudOperation.List);
        var svcCtx = NewSvcCtx();

        // Global hooks run before the cache check so a cache hit cannot silently skip them.
        await _hooks.BeforeGlobalAsync(hookCtx);

        // Check cache only when no per-user security features are active (to avoid serving cached data across users)
        var useCache = _cache is not null && !HasPerUserSecurity(c);
        string? cacheKey = null;
        string? observedListVersion = null;
        if (useCache)
        {
            cacheKey = _cache!.GenerateListCacheKey(resourceKey, spec, expand);
            var cached = await _cache.GetListAsync(resourceKey, cacheKey, ct);
            if (cached is not null)
            {
                await _hooks.AfterGlobalAsync(hookCtx);

                // Cache hits are still reads: keep the audit trail complete.
                if (_security is not null)
                    await LogAuditAsync(_security.CreateAuditEntry(CrudOperation.List, resourceKey), ct);

                sw.Stop();
                DataSurfaceTracing.RecordSuccess(activity, cached.Items.Count);
                _metrics?.RecordOperation(resourceKey, CrudOperation.List, sw.Elapsed.TotalMilliseconds, cached.Items.Count);
                _logger.LogDebug("List {Resource} cache hit, returned {Count}/{Total} items", resourceKey, cached.Items.Count, cached.Total);
                return cached;
            }

            // Observe the list-cache version BEFORE querying so a concurrent invalidation between now
            // and the write-back prevents caching a now-stale result (stale-fill guard).
            observedListVersion = await _cache.GetListVersionAsync(resourceKey, ct);
        }

        if (_features.EnableOverrides && _overrides.TryGet<ListOverride>(c.ResourceKey, CrudOperation.List, out var ov))
        {
            var result = await ov!(c, spec, expand, svcCtx, ct);
            await _hooks.AfterGlobalAsync(hookCtx);
            sw.Stop();
            DataSurfaceTracing.RecordSuccess(activity, result.Items.Count);
            _metrics?.RecordOperation(resourceKey, CrudOperation.List, sw.Elapsed.TotalMilliseconds, result.Items.Count);
            return result;
        }

        var (clrType, set) = ResolveSet(c);

        // Resource-level authorization check (no instance for List)
        if (_security is not null)
            await _security.AuthorizeResourceAsync(c, null, clrType, CrudOperation.List, ct);

        // Apply row-level security filter
        var filteredSet = _security is not null
            ? _security.ApplyResourceFilter(set, clrType, c)
            : set;

        // Apply tenant isolation filter
        if (_security is not null && c.Tenant is not null)
            filteredSet = _security.ApplyTenantFilter(filteredSet, clrType, c);

        // Exclude soft-deleted rows from reads (ISoftDelete) by default, and read no-tracking.
        filteredSet = AsNoTracking(ApplySoftDeleteFilter(filteredSet, clrType));

        var baseQuery = ApplyExpand(filteredSet, c, expand);
        var filtered = ApplyFilterSpec(baseQuery, clrType, c, spec);
        var shaped = ApplyQuerySpec(baseQuery, clrType, c, spec);

        var total = await CountAsync(filtered, ct);
        var pageItems = await ToListAsync(shaped, ct);

        // optional: after-read hook per item (expensive; still useful)
        foreach (var e in pageItems)
            await InvokeTypedAfterRead(e, clrType, hookCtx);

        var json = pageItems.Select(e => EntityToJson(e, c, expand, spec.Fields)).ToList();

        // Apply field-level authorization (redact unauthorized fields)
        _security?.RedactUnauthorizedFields(c, json);

        await _hooks.AfterGlobalAsync(hookCtx);

        // Audit logging for list operation
        if (_security is not null)
            await LogAuditAsync(_security.CreateAuditEntry(CrudOperation.List, resourceKey), ct);

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
            await _cache!.SetListAsync(resourceKey, cacheKey, pagedResult, duration: null, observedVersion: observedListVersion, ct: ct);

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
        using var activity = _features.EnableTracing ? DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.Get, id) : null;
        DataSurfaceTracing.AddExpandInfo(activity, expand?.Expand);

        _logger.LogDebug("Get {Resource} id={Id}", resourceKey, id);

        var c = _contracts.GetByResourceKey(resourceKey);
        EnsureEnabled(c, CrudOperation.Get);

        // Check cache only when no per-user security features are active (to avoid serving cached data across users).
        // Also bypass when the caller requests relation expansions: the get-cache key is only
        // (resource, id), so a cached non-expanded shape must not be served to an expand request.
        // Default expansions are constant per resource, so a request without expand is cacheable.
        var requestsExpand = expand is not null && expand.Expand.Count > 0;

        var hookCtx = NewHookCtx(c, CrudOperation.Get);
        var svcCtx = NewSvcCtx();

        // Global hooks run before the cache check so a cache hit cannot silently skip them.
        await _hooks.BeforeGlobalAsync(hookCtx);

        var useCache = _cache is not null && !HasPerUserSecurity(c) && !requestsExpand;
        if (useCache)
        {
            var cached = await _cache!.GetAsync(resourceKey, id, ct);
            if (cached is not null)
            {
                await _hooks.AfterGlobalAsync(hookCtx);

                // Cache hits are still reads: keep the audit trail complete.
                if (_security is not null)
                    await LogAuditAsync(_security.CreateAuditEntry(CrudOperation.Get, resourceKey, id.ToString()), ct);

                sw.Stop();
                DataSurfaceTracing.RecordSuccess(activity);
                _metrics?.RecordOperation(resourceKey, CrudOperation.Get, sw.Elapsed.TotalMilliseconds);
                _logger.LogDebug("Get {Resource} id={Id} cache hit", resourceKey, id);
                return cached;
            }
        }

        if (_features.EnableOverrides && _overrides.TryGet<GetOverride>(c.ResourceKey, CrudOperation.Get, out var ov))
        {
            var result = await ov!(c, id, expand, svcCtx, ct);
            await _hooks.AfterGlobalAsync(hookCtx);
            sw.Stop();
            DataSurfaceTracing.RecordSuccess(activity, result is null ? 0 : 1);
            _metrics?.RecordOperation(resourceKey, CrudOperation.Get, sw.Elapsed.TotalMilliseconds, result is null ? 0 : 1);
            return result;
        }

        var (clrType, set) = ResolveSet(c);

        // Fast path: a by-id read that needs none of the per-request dynamic query composition
        // (no row-level filter, no tenant scoping, no soft-delete predicate, no relation expansion)
        // collapses to a plain primary-key lookup. Serve it from a cached compiled async (no-tracking)
        // query instead of rebuilding the key-lookup expression tree on every call. When any of those
        // features apply the query shape varies and cannot be precompiled, so use the dynamic path.
        object? entity;
        var expandsRelations = (expand is not null && expand.Expand.Count > 0) || c.Read.DefaultExpand.Count > 0;
        if (_compiledQueries is not null
            && !HasPerUserSecurity(c)
            && !expandsRelations
            && !typeof(ISoftDelete).IsAssignableFrom(clrType))
        {
            var invoker = GetCompiledFindByIdInvoker(clrType, c);
            entity = await invoker(_compiledQueries, _db, id, c.Key.Name, ct);
        }
        else
        {
            // Apply row-level security filter
            var filteredSet = _security is not null
                ? _security.ApplyResourceFilter(set, clrType, c)
                : set;

            // Apply tenant isolation filter
            if (_security is not null && c.Tenant is not null)
                filteredSet = _security.ApplyTenantFilter(filteredSet, clrType, c);

            // Exclude soft-deleted rows from reads (ISoftDelete) by default, and read no-tracking.
            filteredSet = AsNoTracking(ApplySoftDeleteFilter(filteredSet, clrType));

            var q = ApplyExpand(filteredSet, c, expand);

            entity = await FindByIdAsync(q, clrType, c, id, ct);
        }
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

        await InvokeTypedAfterRead(entity, clrType, hookCtx);

        var json = EntityToJson(entity, c, expand);

        // Apply field-level authorization (redact unauthorized fields)
        _security?.RedactUnauthorizedFields(c, json);

        await _hooks.AfterGlobalAsync(hookCtx);

        // Audit logging
        if (_security is not null)
            await LogAuditAsync(_security.CreateAuditEntry(CrudOperation.Get, resourceKey, id.ToString()), ct);

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
        using var activity = _features.EnableTracing ? DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.Create) : null;

        _logger.LogDebug("Create {Resource}", resourceKey);

        var c = _contracts.GetByResourceKey(resourceKey);
        EnsureEnabled(c, CrudOperation.Create);

        ValidateBody(c, CrudOperation.Create, body, _features.EnableFieldValidation);

        // Validate field-level write authorization
        _security?.ValidateFieldWriteAuthorization(c, body, CrudOperation.Create);

        var hookCtx = NewHookCtx(c, CrudOperation.Create);
        var svcCtx = NewSvcCtx();

        await _hooks.BeforeGlobalAsync(hookCtx);

        if (_features.EnableOverrides && _overrides.TryGet<CreateOverride>(c.ResourceKey, CrudOperation.Create, out var ov))
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

        // Resource-level authorization check (the to-be-created instance, before persistence)
        if (_security is not null)
            await _security.AuthorizeResourceAsync(c, entity, clrType, CrudOperation.Create, ct);

        await InvokeTypedBeforeCreate(entity, clrType, body, hookCtx);

        _db.Add(entity);
        await _db.SaveChangesAsync(ct);

        await InvokeTypedAfterCreate(entity, clrType, hookCtx);

        var json = EntityToJson(entity, c, expand: null);

        // Apply field-level authorization (redact unauthorized fields) — the create response
        // is a read of the entity and must not leak fields the caller cannot read.
        _security?.RedactUnauthorizedFields(c, json);

        await _hooks.AfterGlobalAsync(hookCtx);

        // Audit logging
        if (_security is not null)
        {
            var keyVal = GetEntityKeyValue(entity, c);
            await LogAuditAsync(_security.CreateAuditEntry(
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
        using var activity = _features.EnableTracing ? DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.Update, id) : null;

        _logger.LogDebug("Update {Resource} id={Id}", resourceKey, id);

        var c = _contracts.GetByResourceKey(resourceKey);
        EnsureEnabled(c, CrudOperation.Update);
        ValidateBody(c, CrudOperation.Update, patch, _features.EnableFieldValidation);

        // Validate field-level write authorization
        _security?.ValidateFieldWriteAuthorization(c, patch, CrudOperation.Update);

        var hookCtx = NewHookCtx(c, CrudOperation.Update);
        var svcCtx = NewSvcCtx();

        await _hooks.BeforeGlobalAsync(hookCtx);

        if (_features.EnableOverrides && _overrides.TryGet<UpdateOverride>(c.ResourceKey, CrudOperation.Update, out var ov))
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

        // Soft-deleted rows are invisible to reads, so they must not be updatable either.
        filteredSet = ApplySoftDeleteFilter(filteredSet, clrType);

        var entity = await FindByIdAsync(filteredSet, clrType, c, id, ct) ?? throw new CrudNotFoundException(resourceKey, id);

        // Resource-level authorization check
        if (_security is not null)
            await _security.AuthorizeResourceAsync(c, entity, clrType, CrudOperation.Update, ct);

        // Capture previous values for audit
        var previousValues = _security is not null ? EntityToJson(entity, c, expand: null) : null;

        await InvokeTypedBeforeUpdate(entity, clrType, patch, hookCtx);

        InvokeTypedApplyUpdate(entity, clrType, patch, c);
        await _db.SaveChangesAsync(ct);

        await InvokeTypedAfterUpdate(entity, clrType, hookCtx);

        var json = EntityToJson(entity, c, expand: null);

        // Apply field-level authorization (redact unauthorized fields) — the update response
        // is a read of the entity and must not leak fields the caller cannot read.
        _security?.RedactUnauthorizedFields(c, json);

        await _hooks.AfterGlobalAsync(hookCtx);

        // Audit logging
        if (_security is not null)
            await LogAuditAsync(_security.CreateAuditEntry(
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
        using var activity = _features.EnableTracing ? DataSurfaceTracing.StartOperation(resourceKey, CrudOperation.Delete, id) : null;
        activity?.SetTag("datasurface.hard_delete", deleteSpec?.HardDelete ?? false);

        _logger.LogDebug("Delete {Resource} id={Id} hard={Hard}", resourceKey, id, deleteSpec?.HardDelete ?? false);

        var c = _contracts.GetByResourceKey(resourceKey);
        EnsureEnabled(c, CrudOperation.Delete);

        var hookCtx = NewHookCtx(c, CrudOperation.Delete);
        var svcCtx = NewSvcCtx();

        await _hooks.BeforeGlobalAsync(hookCtx);

        if (_features.EnableOverrides && _overrides.TryGet<DeleteOverride>(c.ResourceKey, CrudOperation.Delete, out var ov))
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

        await InvokeTypedBeforeDelete(entity, clrType, hookCtx);

        var hard = deleteSpec?.HardDelete ?? false;

        if (!hard && entity is ISoftDelete sd)
        {
            sd.IsDeleted = true;
            await _db.SaveChangesAsync(ct);

            await InvokeTypedAfterDelete(entity, clrType, hookCtx);
            await _hooks.AfterGlobalAsync(hookCtx);

            // Audit logging
            if (_security is not null)
                await LogAuditAsync(_security.CreateAuditEntry(CrudOperation.Delete, resourceKey, id.ToString()), ct);

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

        await InvokeTypedAfterDelete(entity, clrType, hookCtx);
        await _hooks.AfterGlobalAsync(hookCtx);

        // Audit logging
        if (_security is not null)
            await LogAuditAsync(_security.CreateAuditEntry(CrudOperation.Delete, resourceKey, id.ToString()), ct);

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
            throw new CrudOperationDisabledException(c.ResourceKey, op.ToString());
    }

    private static object? GetEntityKeyValue(object entity, ResourceContract c)
    {
        var prop = entity.GetType().GetProperty(c.Key.Name);
        return prop?.GetValue(entity);
    }
    
    // All typed-hook invokers close the generic over the CONTRACT's CLR type, not
    // entity.GetType(): with EF lazy-loading/change-tracking proxies the runtime type is a
    // proxy subclass, and resolving ICrudHook<ProxyType> from DI would silently match nothing.
    private Task InvokeTypedAfterRead(object entity, Type clrType, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.AfterReadAsync))!
            .MakeGenericMethod(clrType);
        return (Task)m.Invoke(_hooks, new object[] { entity, ctx })!;
    }

    private Task InvokeTypedBeforeCreate(object entity, Type clrType, JsonObject body, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.BeforeCreateAsync))!
            .MakeGenericMethod(clrType);
        return (Task)m.Invoke(_hooks, new object[] { entity, body, ctx })!;
    }

    private Task InvokeTypedAfterCreate(object entity, Type clrType, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.AfterCreateAsync))!
            .MakeGenericMethod(clrType);
        return (Task)m.Invoke(_hooks, new object[] { entity, ctx })!;
    }

    private Task InvokeTypedBeforeUpdate(object entity, Type clrType, JsonObject patch, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.BeforeUpdateAsync))!
            .MakeGenericMethod(clrType);
        return (Task)m.Invoke(_hooks, new object[] { entity, patch, ctx })!;
    }

    private Task InvokeTypedAfterUpdate(object entity, Type clrType, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.AfterUpdateAsync))!
            .MakeGenericMethod(clrType);
        return (Task)m.Invoke(_hooks, new object[] { entity, ctx })!;
    }

    private void InvokeTypedApplyUpdate(object entity, Type clrType, JsonObject patch, ResourceContract c)
    {
        var m = typeof(EfCrudMapper).GetMethod(nameof(EfCrudMapper.ApplyUpdate))!
            .MakeGenericMethod(clrType);
        // DoNotWrapExceptions: let domain exceptions (validation/concurrency) propagate
        // unwrapped instead of being hidden inside TargetInvocationException.
        m.Invoke(_mapper, System.Reflection.BindingFlags.DoNotWrapExceptions, null, new object[] { entity, patch, c, _db }, null);
    }

    private Task InvokeTypedBeforeDelete(object entity, Type clrType, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.BeforeDeleteAsync))!
            .MakeGenericMethod(clrType);
        return (Task)m.Invoke(_hooks, new object[] { entity, ctx })!;
    }

    private Task InvokeTypedAfterDelete(object entity, Type clrType, CrudHookContext ctx)
    {
        var m = typeof(CrudHookDispatcher).GetMethod(nameof(CrudHookDispatcher.AfterDeleteAsync))!
            .MakeGenericMethod(clrType);
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
        // Only treat a security mechanism as "per-user" (cache / fast-path unsafe) when its feature flag is
        // actually enabled — a flag-disabled mechanism is not applied, so caching stays safe and the
        // optimization need not be suppressed. These checks must stay aligned with the gates in
        // CrudSecurityDispatcher (which enforce the same flags).
        if (c.Tenant is not null && _features.EnableTenantIsolation) return true;
        if (_security is null) return false;
        if (_features.EnableFieldAuthorization && _sp.GetService(typeof(IFieldAuthorizer)) is not null) return true;
        if (_features.EnableRowLevelSecurity
            && ((IEnumerable<IResourceFilter>)_sp.GetService(typeof(IEnumerable<IResourceFilter>))!).Any()) return true;
        // Typed row filter / instance authorizer for THIS entity type — these run per request, so a cached
        // result or the compiled by-id fast path must not short-circuit them.
        var clrType = TryResolveClrType(c);
        if (clrType is null) return true; // can't introspect — assume per-user security; use the full path
        if (_features.EnableRowLevelSecurity
            && _sp.GetService(typeof(IResourceFilter<>).MakeGenericType(clrType)) is not null) return true;
        if (_features.EnableResourceAuthorization
            && _sp.GetService(typeof(IResourceAuthorizer<>).MakeGenericType(clrType)) is not null) return true;
        if (_features.EnableResourceAuthorization
            && _sp.GetService(typeof(IResourceAuthorizer)) is not null) return true;
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
        var clrType = ResolveClrType(c);

        var set = (IQueryable)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
            .MakeGenericMethod(clrType)
            .Invoke(_db, null)!;

        return (clrType, set);
    }

    private Type ResolveClrType(ResourceContract c)
        => _typeCache.GetOrAdd(c.ResourceKey, key =>
            // Prefer the DbContext model: it is the authoritative set of entity types this
            // service can operate on, and avoids binding to an unrelated same-named type
            // from an arbitrary loaded assembly.
            _db.Model.GetEntityTypes()
                .Select(et => et.ClrType)
                .FirstOrDefault(t => t.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => SafeGetTypes(a))
                .FirstOrDefault(t => t.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Cannot resolve CLR type for resourceKey '{key}'."));

    private Type? TryResolveClrType(ResourceContract c)
    {
        try { return ResolveClrType(c); }
        catch { return null; }
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
        return (IQueryable)m.Invoke(_query, System.Reflection.BindingFlags.DoNotWrapExceptions, null, new object[] { query, c, spec }, null)!;
    }

    private IQueryable ApplyFilterSpec(IQueryable query, Type clrType, ResourceContract c, QuerySpec spec)
    {
        var m = typeof(EfCrudQueryEngine).GetMethod(nameof(EfCrudQueryEngine.ApplyFiltersAndSort))!
            .MakeGenericMethod(clrType);
        return (IQueryable)m.Invoke(_query, System.Reflection.BindingFlags.DoNotWrapExceptions, null, new object[] { query, c, spec }, null)!;
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
        var constant = System.Linq.Expressions.Expression.Constant(CoerceKey(id, prop.PropertyType), prop.PropertyType);
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

    // Coerces a key value (often an already-typed object from the HTTP layer, or a raw string)
    // to the key property's CLR type. Convert.ChangeType alone throws for Guid/enum, so handle
    // those explicitly; pass through values that are already the target type.
    private static object CoerceKey(object id, Type targetType)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (t.IsInstanceOfType(id)) return id;
        if (t == typeof(string)) return id.ToString()!;
        if (t == typeof(Guid)) return id is Guid g ? g : Guid.Parse(id.ToString()!);
        if (t.IsEnum) return id is string es ? Enum.Parse(t, es, ignoreCase: true) : Enum.ToObject(t, id);
        return Convert.ChangeType(id, t, System.Globalization.CultureInfo.InvariantCulture);
    }

    // Bridges the Type-based service to CompiledQueryCache's generic compiled queries. The strongly-typed
    // invoker is built once per CLR type via Delegate.CreateDelegate (no per-call reflection) and cached.
    private static readonly ConcurrentDictionary<Type, Func<CompiledQueryCache, DbContext, object, string, CancellationToken, Task<object?>>> _compiledFindByIdInvokers = new();

    private static Func<CompiledQueryCache, DbContext, object, string, CancellationToken, Task<object?>> GetCompiledFindByIdInvoker(Type clrType, ResourceContract c)
        => _compiledFindByIdInvokers.GetOrAdd(clrType, t =>
        {
            var keyType = (t.GetProperty(c.Key.Name)
                ?? throw new InvalidOperationException($"Key '{c.Key.Name}' not found on '{t.Name}'.")).PropertyType;
            var mi = typeof(EfDataSurfaceCrudService)
                .GetMethod(nameof(InvokeCompiledFindByIdAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(t, keyType);
            return (Func<CompiledQueryCache, DbContext, object, string, CancellationToken, Task<object?>>)
                Delegate.CreateDelegate(typeof(Func<CompiledQueryCache, DbContext, object, string, CancellationToken, Task<object?>>), mi);
        });

    private static async Task<object?> InvokeCompiledFindByIdAsync<TEntity, TKey>(
        CompiledQueryCache cache, DbContext db, object id, string keyName, CancellationToken ct)
        where TEntity : class
    {
        var compiled = cache.GetOrCreateFindByIdAsyncQuery<TEntity, TKey>(keyName);
        var key = (TKey)CoerceKey(id, typeof(TKey));
        return await compiled(db, key, ct).ConfigureAwait(false);
    }

    // Applies AsNoTracking to a read query — reads never SaveChanges, so EF should not pay the
    // cost of change-tracking the materialized entities.
    private static IQueryable AsNoTracking(IQueryable query)
    {
        var m = typeof(EntityFrameworkQueryableExtensions).GetMethods()
            .First(x => x.Name == nameof(EntityFrameworkQueryableExtensions.AsNoTracking)
                        && x.IsGenericMethodDefinition
                        && x.GetParameters().Length == 1)
            .MakeGenericMethod(query.ElementType);
        return (IQueryable)m.Invoke(null, new object[] { query })!;
    }

    // Applies a "not soft-deleted" predicate to a read query when the entity implements
    // ISoftDelete, so soft-deleted rows are hidden from List/Get by default.
    private static IQueryable ApplySoftDeleteFilter(IQueryable query, Type clrType)
    {
        if (!typeof(ISoftDelete).IsAssignableFrom(clrType)) return query;

        var param = System.Linq.Expressions.Expression.Parameter(clrType, "e");
        var prop = System.Linq.Expressions.Expression.Property(param, nameof(ISoftDelete.IsDeleted));
        var notDeleted = System.Linq.Expressions.Expression.Not(prop);
        var lambda = System.Linq.Expressions.Expression.Lambda(notDeleted, param);

        var where = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.Where) && m.GetParameters().Length == 2)
            .MakeGenericMethod(clrType);

        return (IQueryable)where.Invoke(null, new object[] { query, lambda })!;
    }

    private object CreateEntityByType(Type clrType, JsonObject body, ResourceContract c)
    {
        // Use mapper to create and populate entity
        var m = typeof(EfCrudMapper).GetMethod(nameof(EfCrudMapper.CreateEntity))!
            .MakeGenericMethod(clrType);
        return m.Invoke(_mapper, System.Reflection.BindingFlags.DoNotWrapExceptions, null, new object[] { body, c, _db }, null)!;
    }

    private JsonObject EntityToJson(object entity, ResourceContract c, ExpandSpec? expand, string? projectedFields = null, bool expandRelations = true)
    {
        var o = new JsonObject();
        var readFields = c.Fields.Where(f => f.InRead && !f.Hidden).ToList();

        // Apply field projection if specified
        if (_features.EnableFieldProjection && !string.IsNullOrWhiteSpace(projectedFields))
        {
            var requested = new HashSet<string>(
                projectedFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
            readFields = readFields.Where(f => requested.Contains(f.ApiName)).ToList();
        }

        foreach (var f in readFields)
        {
            // Handle computed fields
            if (_features.EnableComputedFields && f.Computed && !string.IsNullOrWhiteSpace(f.ComputedExpression))
            {
                var computedVal = EvaluateComputedExpression(entity, f.ComputedExpression);
                o[f.ApiName] = ScalarToJson(computedVal);
                continue;
            }

            var p = entity.GetType().GetProperty(f.Name);
            if (p == null) continue;

            var val = p.GetValue(entity);
            o[f.ApiName] = ScalarToJson(val);
        }

        // expand: include nav objects as nested JSON (depth 1)
        // Merge default expanded relations with explicitly requested expansions
        var toSerialize = new HashSet<string>(c.Read.DefaultExpand, StringComparer.OrdinalIgnoreCase);
        if (expand is not null)
        {
            foreach (var e in expand.Expand)
                toSerialize.Add(e);
        }

        if (expandRelations && toSerialize.Count > 0)
        {
            // Derive camelCase convention from the contract: if any field has a PascalCase
            // CLR name but a camelCase API name, the convention is camelCase. Only used for
            // the fallback path (related types that have no resource contract of their own).
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

                // Serialize related entities through THEIR OWN resource contract so that
                // Hidden / non-read / field-authorized fields are not leaked via expand.
                var targetContract = ResolveContractOrNull(rel.TargetResourceKey);

                if (nav is System.Collections.IEnumerable seq && nav is not string)
                {
                    var arr = new JsonArray();
                    foreach (var item in seq.Cast<object>())
                        arr.Add(SerializeRelated(item, targetContract, useCamelCase));
                    o[relApi] = arr;
                }
                else
                {
                    o[relApi] = SerializeRelated(nav, targetContract, useCamelCase);
                }
            }
        }

        return o;
    }

    // Resolves a related resource's contract without throwing (GetByResourceKey throws).
    private ResourceContract? ResolveContractOrNull(string resourceKey)
        => _contracts.All.FirstOrDefault(rc => rc.ResourceKey.Equals(resourceKey, StringComparison.OrdinalIgnoreCase));

    // Serializes a related entity. When the target has its own contract, only that
    // contract's read fields are emitted (depth 1, no further expansion) and field-level
    // authorization is applied — preventing expand from leaking hidden/unauthorized fields.
    // When there is no contract for the target type, falls back to a scalar dump (there is
    // no contract to restrict, so nothing is being bypassed).
    private JsonObject SerializeRelated(object item, ResourceContract? targetContract, bool useCamelCase)
    {
        if (targetContract is null)
            return SimpleObjectToJson(item, useCamelCase);

        var nested = EntityToJson(item, targetContract, expand: null, projectedFields: null, expandRelations: false);
        _security?.RedactUnauthorizedFields(targetContract, nested);
        return nested;
    }

    // Converts a scalar CLR value to a JSON node. A byte[] (typically a rowversion /
    // concurrency token) is emitted as base64 so it round-trips with If-Match / ETag
    // handling, which compares against Convert.ToBase64String(bytes).
    private static JsonNode? ScalarToJson(object? val)
        => val switch
        {
            null => null,
            byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
            _ => JsonValue.Create(val)
        };

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

    // ----- deferred events (bulk transactions) -----

    // When a caller (the bulk service) wraps multiple operations in a transaction, webhooks and
    // audit entries must not be emitted per item: a later rollback would leave external consumers
    // with events for data that never persisted. The scope buffers them; FlushAsync emits after
    // commit, Dispose without flush discards.
    private DeferredEvents? _deferredEvents;

    private sealed class DeferredEvents
    {
        public List<(string ResourceKey, CrudOperation Op, string? Id, JsonObject? Payload)> Webhooks { get; } = new();
        public List<AuditLogEntry> Audits { get; } = new();
    }

    /// <summary>
    /// Begins buffering webhook events and audit entries instead of emitting them immediately.
    /// Call <see cref="DeferredEventsScope.FlushAsync"/> after a successful commit; disposing the
    /// scope without flushing discards the buffered events (rollback semantics).
    /// </summary>
    public DeferredEventsScope BeginDeferredEvents()
    {
        _deferredEvents = new DeferredEvents();
        return new DeferredEventsScope(this);
    }

    /// <summary>
    /// Scope controlling deferred webhook/audit emission. See <see cref="BeginDeferredEvents"/>.
    /// </summary>
    public sealed class DeferredEventsScope : IDisposable
    {
        private readonly EfDataSurfaceCrudService _svc;
        private bool _done;

        internal DeferredEventsScope(EfDataSurfaceCrudService svc) => _svc = svc;

        /// <summary>Emits all buffered events. Call only after the enclosing transaction committed.</summary>
        public async Task FlushAsync(CancellationToken ct = default)
        {
            if (_done) return;
            _done = true;

            var buffered = _svc._deferredEvents;
            _svc._deferredEvents = null;
            if (buffered is null) return;

            if (_svc._security is not null)
            {
                foreach (var entry in buffered.Audits)
                    await _svc._security.LogAuditAsync(entry, ct);
            }

            foreach (var (resourceKey, op, id, payload) in buffered.Webhooks)
                await _svc.PublishWebhookCoreAsync(resourceKey, op, id, payload, ct);
        }

        /// <summary>Discards buffered events when not flushed (i.e. the transaction rolled back).</summary>
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _svc._deferredEvents = null;
        }
    }

    private async Task LogAuditAsync(AuditLogEntry entry, CancellationToken ct)
    {
        if (_deferredEvents is not null)
        {
            _deferredEvents.Audits.Add(entry);
            return;
        }

        if (_security is not null)
            await _security.LogAuditAsync(entry, ct);
    }

    private async Task PublishWebhookAsync(
        string resourceKey,
        CrudOperation operation,
        string? entityId,
        JsonObject? payload,
        CancellationToken ct)
    {
        if (_webhooks is null) return;

        if (_deferredEvents is not null)
        {
            _deferredEvents.Webhooks.Add((resourceKey, operation, entityId, payload));
            return;
        }

        await PublishWebhookCoreAsync(resourceKey, operation, entityId, payload, ct);
    }

    private async Task PublishWebhookCoreAsync(
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

    private static void ValidateBody(ResourceContract c, CrudOperation op, JsonObject body, bool validateFieldConstraints)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var oc = c.Operations[op];

        var allowed = new HashSet<string>(oc.InputShape, StringComparer.OrdinalIgnoreCase);

        // The optimistic-concurrency token (e.g. an If-Match rowversion injected by the HTTP
        // layer) is a valid input on update even though it is not part of the writable update
        // shape. Allow it through here; it is consumed by the concurrency check, not written.
        var concurrencyApiName = op == CrudOperation.Update ? oc.Concurrency?.FieldApiName : null;

        // unknown fields
        foreach (var key in body.Select(kv => kv.Key))
        {
            if (!allowed.Contains(key)
                && !string.Equals(key, concurrencyApiName, StringComparison.OrdinalIgnoreCase))
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
        if (validateFieldConstraints)
            Validation.FieldValidator.ValidateFieldConstraints(c, body, errors);

        if (errors.Count > 0)
            throw new CrudRequestValidationException(errors);
    }
}
