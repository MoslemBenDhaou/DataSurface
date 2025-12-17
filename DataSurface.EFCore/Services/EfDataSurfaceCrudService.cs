using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Context;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Exceptions;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Mapper;
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
    public EfDataSurfaceCrudService(
        DbContext db,
        IResourceContractProvider contracts,
        EfCrudQueryEngine query,
        EfCrudMapper mapper,
        IServiceProvider sp,
        CrudHookDispatcher hooks,
        CrudOverrideRegistry overrides,
        ILogger<EfDataSurfaceCrudService> logger,
        CrudSecurityDispatcher? security = null)
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
        _logger.LogDebug("List {Resource} page={Page} pageSize={PageSize}", resourceKey, spec.Page, spec.PageSize);

        var c = _contracts.GetByResourceKey(resourceKey);
        EnsureEnabled(c, CrudOperation.List);

        var hookCtx = NewHookCtx(c, CrudOperation.List);
        var svcCtx = NewSvcCtx();

        await _hooks.BeforeGlobalAsync(hookCtx);

        if (_overrides.TryGet<ListOverride>(c.ResourceKey, CrudOperation.List, out var ov))
        {
            var result = await ov!(c, spec, expand, svcCtx, ct);
            await _hooks.AfterGlobalAsync(hookCtx);
            return result;
        }

        var (clrType, set) = ResolveSet(c);

        // Apply row-level security filter
        var filteredSet = _security is not null 
            ? _security.ApplyResourceFilter(set, clrType, c) 
            : set;

        var baseQuery = ApplyExpand(filteredSet, c, expand);
        var shaped = ApplyQuerySpec(baseQuery, clrType, c, spec);

        var total = await CountAsync(baseQuery, ct);
        var pageItems = await ToListAsync(shaped, ct);

        // optional: after-read hook per item (expensive; still useful)
        foreach (var e in pageItems)
            await InvokeTypedAfterRead(e, hookCtx);

        var json = pageItems.Select(e => EntityToJson(e, c, expand)).ToList();

        // Apply field-level authorization (redact unauthorized fields)
        _security?.RedactUnauthorizedFields(c, json);

        await _hooks.AfterGlobalAsync(hookCtx);

        // Audit logging for list operation
        if (_security is not null)
            await _security.LogAuditAsync(_security.CreateAuditEntry(CrudOperation.List, resourceKey), ct);

        _logger.LogDebug("List {Resource} completed in {ElapsedMs}ms, returned {Count}/{Total} items",
            resourceKey, sw.ElapsedMilliseconds, json.Count, total);

        return new PagedResult<JsonObject>(
            json,
            Math.Max(1, spec.Page),
            Math.Clamp(spec.PageSize, 1, c.Query.MaxPageSize),
            total);
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
        _logger.LogDebug("Get {Resource} id={Id}", resourceKey, id);

        var c = _contracts.GetByResourceKey(resourceKey);
        EnsureEnabled(c, CrudOperation.Get);

        var hookCtx = NewHookCtx(c, CrudOperation.Get);
        var svcCtx = NewSvcCtx();

        await _hooks.BeforeGlobalAsync(hookCtx);

        if (_overrides.TryGet<GetOverride>(c.ResourceKey, CrudOperation.Get, out var ov))
        {
            var result = await ov!(c, id, expand, svcCtx, ct);
            await _hooks.AfterGlobalAsync(hookCtx);
            return result;
        }

        var (clrType, set) = ResolveSet(c);

        // Apply row-level security filter
        var filteredSet = _security is not null 
            ? _security.ApplyResourceFilter(set, clrType, c) 
            : set;

        var q = ApplyExpand(filteredSet, c, expand);

        var entity = await FindByIdAsync(q, clrType, c, id, ct);
        if (entity is null)
        {
            await _hooks.AfterGlobalAsync(hookCtx);
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

        _logger.LogDebug("Get {Resource} id={Id} completed in {ElapsedMs}ms", resourceKey, id, sw.ElapsedMilliseconds);
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
            return result;
        }

        var (clrType, _) = ResolveSet(c);
        var entity = CreateEntityByType(clrType, body, c);

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
            return result;
        }

        var (clrType, set) = ResolveSet(c);

        // Apply row-level security filter
        var filteredSet = _security is not null 
            ? _security.ApplyResourceFilter(set, clrType, c) 
            : set;

        var entity = await FindByIdAsync(filteredSet, clrType, c, id, ct) ?? throw new CrudNotFoundException(resourceKey, id);

        // Resource-level authorization check
        if (_security is not null)
            await _security.AuthorizeResourceAsync(c, entity, clrType, CrudOperation.Update, ct);

        // Capture previous values for audit
        var previousValues = _security is not null ? EntityToJson(entity, c, expand: null) : null;

        await InvokeTypedBeforeUpdate(entity, patch, hookCtx);

        _mapper.ApplyUpdate(entity, patch, c, _db);
        await _db.SaveChangesAsync(ct);

        await InvokeTypedAfterUpdate(entity, hookCtx);

        var json = EntityToJson(entity, c, expand: null);

        await _hooks.AfterGlobalAsync(hookCtx);

        // Audit logging
        if (_security is not null)
            await _security.LogAuditAsync(_security.CreateAuditEntry(
                CrudOperation.Update, resourceKey, id.ToString(), changes: patch, previousValues: previousValues), ct);

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
            return;
        }

        var (clrType, set) = ResolveSet(c);

        // Apply row-level security filter
        var filteredSet = _security is not null 
            ? _security.ApplyResourceFilter(set, clrType, c) 
            : set;

        var entity = await FindByIdAsync(filteredSet, clrType, c, id, ct) ?? throw new CrudNotFoundException(resourceKey, id);

        // Resource-level authorization check
        if (_security is not null)
            await _security.AuthorizeResourceAsync(c, entity, clrType, CrudOperation.Delete, ct);

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
        if (expand is null || expand.Expand.Count == 0) return query;

        var allowed = new HashSet<string>(c.Read.ExpandAllowed, StringComparer.OrdinalIgnoreCase);
        foreach (var e in expand.Expand)
        {
            if (!allowed.Contains(e)) continue;

            // use string-based Include to avoid generic constraints
            query = (IQueryable)typeof(EntityFrameworkQueryableExtensions)
                .GetMethods()
                .Single(m => m.Name == "Include"
                             && m.IsGenericMethodDefinition
                             && m.GetGenericArguments().Length == 1
                             && m.GetParameters().Length == 2
                             && m.GetParameters()[1].ParameterType == typeof(string))
                .MakeGenericMethod(query.ElementType)
                .Invoke(null, new object?[] { query, e })!;

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

    private static JsonObject EntityToJson(object entity, ResourceContract c, ExpandSpec? expand)
    {
        var o = new JsonObject();
        var readFields = c.Fields.Where(f => f.InRead && !f.Hidden).ToList();

        foreach (var f in readFields)
        {
            var p = entity.GetType().GetProperty(f.Name);
            if (p == null) continue;

            var val = p.GetValue(entity);
            o[f.ApiName] = val is null ? null : JsonValue.Create(val);
        }

        // expand: include nav objects as nested JSON (depth 1)
        if (expand is not null && expand.Expand.Count > 0)
        {
            var allowed = new HashSet<string>(c.Read.ExpandAllowed, StringComparer.OrdinalIgnoreCase);
            foreach (var relApi in expand.Expand.Where(x => allowed.Contains(x)))
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
                        arr.Add(SimpleObjectToJson(item));
                    o[relApi] = arr;
                }
                else
                {
                    o[relApi] = SimpleObjectToJson(nav);
                }
            }
        }

        return o;
    }

    private static JsonObject SimpleObjectToJson(object obj)
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

            j[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = JsonValue.Create(p.GetValue(obj));
        }
        return j;
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

        // immutable on update
        if (op == CrudOperation.Update)
        {
            foreach (var imm in oc.ImmutableFields)
            {
                if (body.ContainsKey(imm))
                    errors[imm] = new[] { "Field is immutable." };
            }

            // concurrency required
            if (oc.Concurrency is { RequiredOnUpdate: true } cc)
            {
                if (!body.ContainsKey(cc.FieldApiName))
                    errors[cc.FieldApiName] = new[] { "Concurrency token is required." };
            }
        }

        if (errors.Count > 0)
            throw new CrudRequestValidationException(errors);
    }
}
