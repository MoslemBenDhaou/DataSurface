using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.Dynamic.Contracts;
using DataSurface.Dynamic.Entities;
using DataSurface.Dynamic.Hooks;
using DataSurface.Dynamic.Indexing;
using DataSurface.EFCore.Context;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Exceptions;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataSurface.Dynamic.Services;

/// <summary>
/// Dynamic JSON-backed implementation of <see cref="IDataSurfaceCrudService"/>.
/// </summary>
public sealed class DynamicDataSurfaceCrudService : IDataSurfaceCrudService
{
    private readonly DbContext _db;
    private readonly DynamicResourceContractProvider _contracts;
    private readonly IDynamicIndexService _index;
    private readonly IServiceProvider _sp;

    private readonly CrudHookDispatcher _globalHooks;
    private readonly CrudResourceHookDispatcher _resourceHooks;
    private readonly CrudOverrideRegistry _overrides;
    private readonly ILogger<DynamicDataSurfaceCrudService> _logger;

    private readonly IResourceContractProvider _compositeContracts; // for expand targets

    private readonly JsonSerializerOptions _jsonOpt = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates a new dynamic CRUD service.
    /// </summary>
    /// <param name="db">The EF Core database context used to store dynamic records and metadata.</param>
    /// <param name="contracts">Provider for dynamic resource contracts.</param>
    /// <param name="compositeContracts">Provider used to resolve expansion targets across backends.</param>
    /// <param name="index">Indexing service used to maintain filter/sort indexes.</param>
    /// <param name="sp">The service provider.</param>
    /// <param name="globalHooks">Dispatcher for global hooks.</param>
    /// <param name="resourceHooks">Dispatcher for resource-specific hooks.</param>
    /// <param name="overrides">Registry of per-resource override delegates.</param>
    /// <param name="logger">The logger instance.</param>
    public DynamicDataSurfaceCrudService(
        DbContext db,
        DynamicResourceContractProvider contracts,
        IResourceContractProvider compositeContracts,
        IDynamicIndexService index,
        IServiceProvider sp,
        CrudHookDispatcher globalHooks,
        CrudResourceHookDispatcher resourceHooks,
        CrudOverrideRegistry overrides,
        ILogger<DynamicDataSurfaceCrudService> logger)
    {
        _db = db;
        _contracts = contracts;
        _compositeContracts = compositeContracts;
        _index = index;
        _sp = sp;
        _globalHooks = globalHooks;
        _resourceHooks = resourceHooks;
        _overrides = overrides;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <summary>
    /// Retrieves a list of records for the specified resource.
    /// </summary>
    /// <param name="resourceKey">The key of the resource to retrieve records for.</param>
    /// <param name="spec">The query specification.</param>
    /// <param name="expand">The expansion specification.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A paged result containing the retrieved records.</returns>
    public async Task<PagedResult<JsonObject>> ListAsync(string resourceKey, QuerySpec spec, ExpandSpec? expand = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Dynamic List {Resource} page={Page} pageSize={PageSize}", resourceKey, spec.Page, spec.PageSize);

        var c = await _contracts.GetByResourceKeyAsync(resourceKey, ct);
        EnsureEnabled(c, CrudOperation.List);

        var hookCtx = NewHookCtx(c, CrudOperation.List);
        var svcCtx = NewSvcCtx();

        await _globalHooks.BeforeGlobalAsync(hookCtx);

        if (_overrides.TryGet<ListOverride>(c.ResourceKey, CrudOperation.List, out var ov))
        {
            var result = await ov!(c, spec, expand, svcCtx, ct);
            await _globalHooks.AfterGlobalAsync(hookCtx);
            return result;
        }

        var baseQuery = _db.Set<DsDynamicRecordRow>()
            .AsNoTracking()
            .Where(r => r.EntityKey == c.ResourceKey && !r.IsDeleted);

        var filtered = ApplyFilters(baseQuery, c, spec);
        var total = await filtered.CountAsync(ct);

        var sorted = ApplySort(filtered, c, spec);

        var page = Math.Max(1, spec.Page);
        var pageSize = Math.Clamp(spec.PageSize, 1, c.Query.MaxPageSize);

        var rows = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Parse field projection
        string? projectedFields = spec.Fields;

        var items = new List<JsonObject>(rows.Count);
        foreach (var row in rows)
        {
            var obj = ProjectRowToJson(row, c, projectedFields);
            if (expand is not null) await ApplyExpandAsync(obj, row, c, expand, ct);
            // JSON read hook
            await _resourceHooks.AfterReadAsync(c.ResourceKey, row.Id, obj, hookCtx);
            items.Add(obj);
        }

        await _globalHooks.AfterGlobalAsync(hookCtx);

        _logger.LogDebug("Dynamic List {Resource} completed in {ElapsedMs}ms, returned {Count}/{Total} items",
            resourceKey, sw.ElapsedMilliseconds, items.Count, total);

        return new PagedResult<JsonObject>(items, page, pageSize, total);
    }

    /// <inheritdoc />
    /// <summary>
    /// Retrieves a single record for the specified resource.
    /// </summary>
    /// <param name="resourceKey">The key of the resource to retrieve the record for.</param>
    /// <param name="id">The ID of the record to retrieve.</param>
    /// <param name="expand">The expansion specification.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The retrieved record, or null if not found.</returns>
    public async Task<JsonObject?> GetAsync(string resourceKey, object id, ExpandSpec? expand = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Dynamic Get {Resource} id={Id}", resourceKey, id);

        var c = await _contracts.GetByResourceKeyAsync(resourceKey, ct);
        EnsureEnabled(c, CrudOperation.Get);

        var hookCtx = NewHookCtx(c, CrudOperation.Get);
        var svcCtx = NewSvcCtx();

        await _globalHooks.BeforeGlobalAsync(hookCtx);

        if (_overrides.TryGet<GetOverride>(c.ResourceKey, CrudOperation.Get, out var ov))
        {
            var result = await ov!(c, id, expand, svcCtx, ct);
            await _globalHooks.AfterGlobalAsync(hookCtx);
            return result;
        }

        var idStr = NormalizeIdString(id);
        var row = await _db.Set<DsDynamicRecordRow>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.EntityKey == c.ResourceKey && r.Id == idStr && !r.IsDeleted, ct);

        if (row is null)
        {
            await _globalHooks.AfterGlobalAsync(hookCtx);
            return null;
        }

        var obj = ProjectRowToJson(row, c);
        if (expand is not null) await ApplyExpandAsync(obj, row, c, expand, ct);

        await _resourceHooks.AfterReadAsync(c.ResourceKey, row.Id, obj, hookCtx);
        await _globalHooks.AfterGlobalAsync(hookCtx);

        _logger.LogDebug("Dynamic Get {Resource} id={Id} completed in {ElapsedMs}ms", resourceKey, id, sw.ElapsedMilliseconds);
        return obj;
    }

    /// <inheritdoc />
    /// <summary>
    /// Creates a new record for the specified resource.
    /// </summary>
    /// <param name="resourceKey">The key of the resource to create the record for.</param>
    /// <param name="body">The JSON body of the record to create.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The created record.</returns>
    public async Task<JsonObject> CreateAsync(string resourceKey, JsonObject body, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Dynamic Create {Resource}", resourceKey);

        var c = await _contracts.GetByResourceKeyAsync(resourceKey, ct);
        EnsureEnabled(c, CrudOperation.Create);

        ValidateBody(c, CrudOperation.Create, body);

        var hookCtx = NewHookCtx(c, CrudOperation.Create);
        var svcCtx = NewSvcCtx();

        await _globalHooks.BeforeGlobalAsync(hookCtx);

        if (_overrides.TryGet<CreateOverride>(c.ResourceKey, CrudOperation.Create, out var ov))
        {
            var result = await ov!(c, body, svcCtx, ct);
            await _globalHooks.AfterGlobalAsync(hookCtx);
            return result;
        }

        await _resourceHooks.BeforeCreateAsync(c.ResourceKey, body, hookCtx);

        // Determine record Id
        var keyApi = GetKeyApiName(c);
        var recordId = ResolveOrGenerateId(c, body);

        // Build stored JSON (only allowed Create fields)
        var stored = BuildStoredJson(c, CrudOperation.Create, body);
        stored[keyApi] = recordId;

        var row = new DsDynamicRecordRow
        {
            EntityKey = c.ResourceKey,
            Id = recordId,
            DataJson = stored.ToJsonString(),
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Add(row);
        await _db.SaveChangesAsync(ct);

        // rebuild indexes
        await _index.RebuildIndexesAsync(c.ResourceKey, row.Id, c, stored, ct);

        var created = ProjectJsonToReadShape(c, stored);
        await _resourceHooks.AfterCreateAsync(c.ResourceKey, created, hookCtx);

        await _globalHooks.AfterGlobalAsync(hookCtx);

        _logger.LogInformation("Dynamic Created {Resource} id={Id} in {ElapsedMs}ms", resourceKey, recordId, sw.ElapsedMilliseconds);
        return created;
    }

    /// <inheritdoc />
    /// <summary>
    /// Updates an existing record for the specified resource.
    /// </summary>
    /// <param name="resourceKey">The key of the resource to update the record for.</param>
    /// <param name="id">The ID of the record to update.</param>
    /// <param name="patch">The JSON patch to apply to the record.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated record.</returns>
    public async Task<JsonObject> UpdateAsync(string resourceKey, object id, JsonObject patch, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Dynamic Update {Resource} id={Id}", resourceKey, id);

        var c = await _contracts.GetByResourceKeyAsync(resourceKey, ct);
        EnsureEnabled(c, CrudOperation.Update);

        ValidateBody(c, CrudOperation.Update, patch);

        var hookCtx = NewHookCtx(c, CrudOperation.Update);
        var svcCtx = NewSvcCtx();

        await _globalHooks.BeforeGlobalAsync(hookCtx);

        if (_overrides.TryGet<UpdateOverride>(c.ResourceKey, CrudOperation.Update, out var ov))
        {
            var result = await ov!(c, id, patch, svcCtx, ct);
            await _globalHooks.AfterGlobalAsync(hookCtx);
            return result;
        }

        var idStr = NormalizeIdString(id);

        // Load tracked entity for concurrency
        var row = await _db.Set<DsDynamicRecordRow>()
            .FirstOrDefaultAsync(r => r.EntityKey == c.ResourceKey && r.Id == idStr && !r.IsDeleted, ct);

        if (row is null) throw new CrudNotFoundException(resourceKey, id);

        await _resourceHooks.BeforeUpdateAsync(c.ResourceKey, id, patch, hookCtx);

        // Concurrency (RowVersion)
        ApplyConcurrencyTokenIfAny(c, patch, row);

        var current = JsonNode.Parse(row.DataJson)?.AsObject() ?? new JsonObject();
        var keyApi = GetKeyApiName(c);

        // Apply patch only for allowed fields
        var stored = BuildStoredJson(c, CrudOperation.Update, patch, current);
        stored[keyApi] = row.Id; // keep key stable

        row.DataJson = stored.ToJsonString();
        row.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _index.RebuildIndexesAsync(c.ResourceKey, row.Id, c, stored, ct);

        var updated = ProjectJsonToReadShape(c, stored);
        await _resourceHooks.AfterUpdateAsync(c.ResourceKey, id, updated, hookCtx);

        await _globalHooks.AfterGlobalAsync(hookCtx);

        _logger.LogInformation("Dynamic Updated {Resource} id={Id} in {ElapsedMs}ms", resourceKey, id, sw.ElapsedMilliseconds);
        return updated;
    }

    /// <inheritdoc />
    /// <summary>
    /// Deletes a record for the specified resource.
    /// </summary>
    /// <param name="resourceKey">The key of the resource to delete the record for.</param>
    /// <param name="id">The ID of the record to delete.</param>
    /// <param name="deleteSpec">The delete specification.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task DeleteAsync(string resourceKey, object id, CrudDeleteSpec? deleteSpec = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Dynamic Delete {Resource} id={Id} hard={Hard}", resourceKey, id, deleteSpec?.HardDelete ?? false);

        var c = await _contracts.GetByResourceKeyAsync(resourceKey, ct);
        EnsureEnabled(c, CrudOperation.Delete);

        var hookCtx = NewHookCtx(c, CrudOperation.Delete);
        var svcCtx = NewSvcCtx();

        await _globalHooks.BeforeGlobalAsync(hookCtx);

        if (_overrides.TryGet<DeleteOverride>(c.ResourceKey, CrudOperation.Delete, out var ov))
        {
            await ov!(c, id, deleteSpec, svcCtx, ct);
            await _globalHooks.AfterGlobalAsync(hookCtx);
            return;
        }

        var idStr = NormalizeIdString(id);
        var row = await _db.Set<DsDynamicRecordRow>()
            .FirstOrDefaultAsync(r => r.EntityKey == c.ResourceKey && r.Id == idStr && !r.IsDeleted, ct);

        if (row is null) throw new CrudNotFoundException(resourceKey, id);

        await _resourceHooks.BeforeDeleteAsync(c.ResourceKey, id, hookCtx);

        var hard = deleteSpec?.HardDelete ?? false;

        if (!hard)
        {
            row.IsDeleted = true;
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // indexes can remain (filtered out by IsDeleted) OR be deleted.
            await _resourceHooks.AfterDeleteAsync(c.ResourceKey, id, hookCtx);
            await _globalHooks.AfterGlobalAsync(hookCtx);

            _logger.LogInformation("Dynamic Soft-deleted {Resource} id={Id} in {ElapsedMs}ms", resourceKey, id, sw.ElapsedMilliseconds);
            return;
        }

        // hard delete: remove record and indexes
        var oldIdx = _db.Set<DsDynamicIndexRow>().Where(x => x.EntityKey == c.ResourceKey && x.RecordId == row.Id);
        _db.RemoveRange(oldIdx);
        _db.Remove(row);
        await _db.SaveChangesAsync(ct);

        await _resourceHooks.AfterDeleteAsync(c.ResourceKey, id, hookCtx);
        await _globalHooks.AfterGlobalAsync(hookCtx);

        _logger.LogInformation("Dynamic Deleted {Resource} id={Id} in {ElapsedMs}ms", resourceKey, id, sw.ElapsedMilliseconds);
    }

    // ---------------- helpers ----------------

    private CrudHookContext NewHookCtx(ResourceContract c, CrudOperation op)
        => new() { Operation = op, Contract = c, Db = _db, Services = _sp };

    private CrudServiceContext NewSvcCtx()
        => new() { Services = _sp, Db = _db, Mapper = null!, Query = null!, Contracts = _compositeContracts };
    // Mapper/Query are not used by this dynamic service overrides unless you want; set to null! or provide if available.

    private static void EnsureEnabled(ResourceContract c, CrudOperation op)
    {
        if (!c.Operations.TryGetValue(op, out var oc) || !oc.Enabled)
            throw new InvalidOperationException($"Operation '{op}' is disabled for resource '{c.ResourceKey}'.");
    }

    private static string NormalizeIdString(object id)
        => id is string s ? s : Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture) ?? id.ToString()!;

    private static string GetKeyApiName(ResourceContract c)
    {
        var keyField = c.Fields.FirstOrDefault(f => f.Name.Equals(c.Key.Name, StringComparison.OrdinalIgnoreCase));
        return keyField?.ApiName ?? c.Key.Name;
    }

    private string ResolveOrGenerateId(ResourceContract c, JsonObject body)
    {
        var keyApi = GetKeyApiName(c);

        if (body.TryGetPropertyValue(keyApi, out var n) && n is not null)
            return n.ToJsonString().Trim('"');

        // If not supplied, only auto-generate for Guid keys
        if (c.Key.Type == FieldType.Guid)
            return Guid.NewGuid().ToString("N");

        throw new CrudRequestValidationException(new Dictionary<string, string[]>
        {
            [keyApi] = new[] { "Key is required for this entity (no auto-generation configured)." }
        });
    }

    private static JsonObject ProjectRowToJson(DsDynamicRecordRow row, ResourceContract contract, string? projectedFields = null)
    {
        var obj = JsonNode.Parse(row.DataJson)?.AsObject() ?? new JsonObject();
        return ProjectJsonToReadShape(contract, obj, projectedFields);
    }

    private static JsonObject ProjectJsonToReadShape(ResourceContract c, JsonObject json, string? projectedFields = null)
    {
        var o = new JsonObject();
        var readFields = c.Fields.Where(f => f.InRead && !f.Hidden);

        // Apply field projection if specified
        if (!string.IsNullOrWhiteSpace(projectedFields))
        {
            var requested = new HashSet<string>(
                projectedFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
            readFields = readFields.Where(f => requested.Contains(f.ApiName));
        }

        foreach (var f in readFields)
        {
            if (json.TryGetPropertyValue(f.ApiName, out var v))
                o[f.ApiName] = v?.DeepClone();
            else
                o[f.ApiName] = null;
        }
        return o;
    }

    private JsonObject BuildStoredJson(ResourceContract c, CrudOperation op, JsonObject input, JsonObject? existing = null)
    {
        // Stored representation is merged (PATCH-like) for Update.
        var stored = existing?.DeepClone().AsObject() ?? new JsonObject();
        var allowed = new HashSet<string>(c.Operations[op].InputShape, StringComparer.OrdinalIgnoreCase);

        foreach (var kv in input)
        {
            if (!allowed.Contains(kv.Key)) continue;
            stored[kv.Key] = kv.Value?.DeepClone();
        }

        return stored;
    }

    private static void ApplyConcurrencyTokenIfAny(ResourceContract c, JsonObject patch, DsDynamicRecordRow row)
    {
        var cc = c.Operations[CrudOperation.Update].Concurrency;
        if (cc is null || cc.Mode == ConcurrencyMode.None) return;

        if (!patch.TryGetPropertyValue(cc.FieldApiName, out var tokenNode) || tokenNode is null)
        {
            if (cc.RequiredOnUpdate)
                throw new CrudRequestValidationException(new Dictionary<string, string[]>
                {
                    [cc.FieldApiName] = new[] { "Concurrency token is required." }
                });
            return;
        }

        if (cc.Mode == ConcurrencyMode.RowVersion)
        {
            var tokenStr = tokenNode.ToJsonString().Trim('"');
            var bytes = Convert.FromBase64String(tokenStr);

            // EF concurrency: set original value
            var entry = row; // tracked
            // Use EF Entry API via DbContext in caller if needed; simplified:
            // We can also set OriginalValue by attaching: done best in caller.
            // Here, we validate equality to avoid silent overwrite:
            if (!row.RowVersion.SequenceEqual(bytes))
                throw new DbUpdateConcurrencyException("Concurrency conflict (rowversion mismatch).");
        }
    }

    private IQueryable<DsDynamicRecordRow> ApplyFilters(IQueryable<DsDynamicRecordRow> baseQuery, ResourceContract c, QuerySpec spec)
    {
        if (spec.Filters is null || spec.Filters.Count == 0) return baseQuery;

        var allowed = new HashSet<string>(c.Query.FilterableFields, StringComparer.OrdinalIgnoreCase);

        IQueryable<string>? idSet = null;

        foreach (var (apiField, raw) in spec.Filters)
        {
            if (!allowed.Contains(apiField)) continue;

            var field = c.Fields.FirstOrDefault(f => f.ApiName.Equals(apiField, StringComparison.OrdinalIgnoreCase));
            if (field is null) continue;

            var ids = FilterIdsForField(c.ResourceKey, apiField, field.Type, raw);

            idSet = idSet is null ? ids : idSet.Intersect(ids);
        }

        if (idSet is null) return baseQuery;

        return baseQuery.Where(r => idSet.Contains(r.Id));
    }

    private IQueryable<string> FilterIdsForField(string entityKey, string apiName, FieldType type, string raw)
    {
        var (op, value) = ParseOp(raw);

        var idx = _db.Set<DsDynamicIndexRow>().AsNoTracking()
            .Where(i => i.EntityKey == entityKey && i.PropertyApiName == apiName);

        // Important: only index rows exist for indexed fields; if a field isn't indexed, filter returns empty -> correct.
        return type switch
        {
            FieldType.Int32 or FieldType.Int64 or FieldType.Decimal
                => FilterNumber(idx, op, value).Select(i => i.RecordId),

            FieldType.DateTime
                => FilterDate(idx, op, value).Select(i => i.RecordId),

            FieldType.Boolean
                => FilterBool(idx, op, value).Select(i => i.RecordId),

            FieldType.Guid
                => FilterGuid(idx, op, value).Select(i => i.RecordId),

            _ => FilterString(idx, op, value).Select(i => i.RecordId),
        };
    }

    private static IQueryable<DsDynamicIndexRow> FilterNumber(IQueryable<DsDynamicIndexRow> q, string op, string val)
    {
        if (!decimal.TryParse(val, System.Globalization.CultureInfo.InvariantCulture, out var n))
            throw new CrudRequestValidationException(new Dictionary<string, string[]>
            {
                ["filter"] = new[] { $"Invalid numeric filter value '{val}'." }
            });
        
        return op switch
        {
            "eq" => q.Where(x => x.ValueNumber == n),
            "neq" => q.Where(x => x.ValueNumber != n),
            "gt" => q.Where(x => x.ValueNumber > n),
            "gte" => q.Where(x => x.ValueNumber >= n),
            "lt" => q.Where(x => x.ValueNumber < n),
            "lte" => q.Where(x => x.ValueNumber <= n),
            _ => q.Where(x => x.ValueNumber == n),
        };
    }

    private static IQueryable<DsDynamicIndexRow> FilterDate(IQueryable<DsDynamicIndexRow> q, string op, string val)
    {
        if (!DateTime.TryParse(val, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var d))
            throw new CrudRequestValidationException(new Dictionary<string, string[]>
            {
                ["filter"] = new[] { $"Invalid date filter value '{val}'." }
            });
        
        return op switch
        {
            "eq" => q.Where(x => x.ValueDateTime == d),
            "gt" => q.Where(x => x.ValueDateTime > d),
            "gte" => q.Where(x => x.ValueDateTime >= d),
            "lt" => q.Where(x => x.ValueDateTime < d),
            "lte" => q.Where(x => x.ValueDateTime <= d),
            _ => q.Where(x => x.ValueDateTime == d),
        };
    }

    private static IQueryable<DsDynamicIndexRow> FilterBool(IQueryable<DsDynamicIndexRow> q, string op, string val)
    {
        if (!bool.TryParse(val, out var b))
            throw new CrudRequestValidationException(new Dictionary<string, string[]>
            {
                ["filter"] = new[] { $"Invalid boolean filter value '{val}'." }
            });
        
        return op switch
        {
            "eq" => q.Where(x => x.ValueBool == b),
            "neq" => q.Where(x => x.ValueBool != b),
            _ => q.Where(x => x.ValueBool == b),
        };
    }

    private static IQueryable<DsDynamicIndexRow> FilterGuid(IQueryable<DsDynamicIndexRow> q, string op, string val)
    {
        if (!Guid.TryParse(val, out var g))
            throw new CrudRequestValidationException(new Dictionary<string, string[]>
            {
                ["filter"] = new[] { $"Invalid GUID filter value '{val}'." }
            });
        
        return op switch
        {
            "eq" => q.Where(x => x.ValueGuid == g),
            "neq" => q.Where(x => x.ValueGuid != g),
            _ => q.Where(x => x.ValueGuid == g),
        };
    }

    private static IQueryable<DsDynamicIndexRow> FilterString(IQueryable<DsDynamicIndexRow> q, string op, string val)
    {
        return op switch
        {
            "eq" => q.Where(x => x.ValueString == val),
            "neq" => q.Where(x => x.ValueString != val),
            "contains" => q.Where(x => x.ValueString != null && x.ValueString.Contains(val)),
            "starts" => q.Where(x => x.ValueString != null && x.ValueString.StartsWith(val)),
            "ends" => q.Where(x => x.ValueString != null && x.ValueString.EndsWith(val)),
            "in" => FilterStringIn(q, val),
            _ => q.Where(x => x.ValueString == val),
        };
    }

    private static IQueryable<DsDynamicIndexRow> FilterStringIn(IQueryable<DsDynamicIndexRow> q, string val)
    {
        var values = val.Split('|'); // Split outside the expression tree
        return q.Where(x => x.ValueString != null && values.Contains(x.ValueString));
    }

    private IQueryable<DsDynamicRecordRow> ApplySort(IQueryable<DsDynamicRecordRow> query, ResourceContract c, QuerySpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Sort))
            return query.OrderByDescending(r => r.UpdatedAt);

        var parts = spec.Sort!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return query.OrderByDescending(r => r.UpdatedAt);

        // Collect valid sort fields first
        var sortFields = new List<(FieldContract field, string api, bool desc)>();
        foreach (var part in parts)
        {
            var desc = part.StartsWith("-");
            var api = desc ? part[1..] : part;
            if (!c.Query.SortableFields.Contains(api, StringComparer.OrdinalIgnoreCase)) continue;
            var f = c.Fields.FirstOrDefault(x => x.ApiName.Equals(api, StringComparison.OrdinalIgnoreCase));
            if (f is not null) sortFields.Add((f, api, desc));
        }

        if (sortFields.Count == 0) return query.OrderByDescending(r => r.UpdatedAt);

        // Use correlated subquery sorting with proper OrderBy/ThenBy chaining
        // so that multiple sort fields produce a compound ORDER BY clause.
        IOrderedQueryable<DsDynamicRecordRow>? ordered = null;
        foreach (var (f, api, isDesc) in sortFields)
        {
            var ek = c.ResourceKey;
            var pa = api;
            var isFirst = ordered is null;
            IQueryable<DsDynamicRecordRow> src = ordered ?? query;

            ordered = f.Type switch
            {
                FieldType.Int32 or FieldType.Int64 or FieldType.Decimal =>
                    ApplySortField(src, r => _db.Set<DsDynamicIndexRow>()
                        .Where(i => i.EntityKey == ek && i.RecordId == r.Id && i.PropertyApiName == pa)
                        .Select(i => i.ValueNumber).FirstOrDefault(), isDesc, isFirst),
                FieldType.DateTime =>
                    ApplySortField(src, r => _db.Set<DsDynamicIndexRow>()
                        .Where(i => i.EntityKey == ek && i.RecordId == r.Id && i.PropertyApiName == pa)
                        .Select(i => i.ValueDateTime).FirstOrDefault(), isDesc, isFirst),
                FieldType.Boolean =>
                    ApplySortField(src, r => _db.Set<DsDynamicIndexRow>()
                        .Where(i => i.EntityKey == ek && i.RecordId == r.Id && i.PropertyApiName == pa)
                        .Select(i => i.ValueBool).FirstOrDefault(), isDesc, isFirst),
                FieldType.Guid =>
                    ApplySortField(src, r => _db.Set<DsDynamicIndexRow>()
                        .Where(i => i.EntityKey == ek && i.RecordId == r.Id && i.PropertyApiName == pa)
                        .Select(i => i.ValueGuid).FirstOrDefault(), isDesc, isFirst),
                _ =>
                    ApplySortField(src, r => _db.Set<DsDynamicIndexRow>()
                        .Where(i => i.EntityKey == ek && i.RecordId == r.Id && i.PropertyApiName == pa)
                        .Select(i => i.ValueString).FirstOrDefault(), isDesc, isFirst),
            };
        }

        return ordered!;
    }

    private static IOrderedQueryable<DsDynamicRecordRow> ApplySortField<TKey>(
        IQueryable<DsDynamicRecordRow> query,
        System.Linq.Expressions.Expression<Func<DsDynamicRecordRow, TKey>> keySelector,
        bool desc, bool first)
    {
        if (first)
            return desc ? query.OrderByDescending(keySelector) : query.OrderBy(keySelector);
        return desc
            ? ((IOrderedQueryable<DsDynamicRecordRow>)query).ThenByDescending(keySelector)
            : ((IOrderedQueryable<DsDynamicRecordRow>)query).ThenBy(keySelector);
    }

    private static (string op, string value) ParseOp(string raw)
    {
        var idx = raw.IndexOf(':');
        if (idx <= 0) return ("eq", raw.Trim());
        return (raw[..idx].Trim().ToLowerInvariant(), raw[(idx + 1)..].Trim());
    }

    private async Task ApplyExpandAsync(JsonObject projected, DsDynamicRecordRow row, ResourceContract contract, ExpandSpec expand, CancellationToken ct)
    {
        if (expand.Expand.Count == 0) return;

        var allowed = new HashSet<string>(contract.Read.ExpandAllowed, StringComparer.OrdinalIgnoreCase);

        var obj = JsonNode.Parse(row.DataJson)?.AsObject() ?? new JsonObject();

        foreach (var relApi in expand.Expand.Where(allowed.Contains))
        {
            var rel = contract.Relations.FirstOrDefault(r => r.ApiName.Equals(relApi, StringComparison.OrdinalIgnoreCase));
            if (rel is null) continue;

            // Only dynamic-to-dynamic expansion in phase 4
            var targetContract = _compositeContracts.GetByResourceKey(rel.TargetResourceKey);
            if (targetContract.Backend != StorageBackend.DynamicJson)
            {
                // TODO: cross-backend expansion
                continue;
            }

            var writeField = rel.Write.WriteFieldName;
            if (string.IsNullOrWhiteSpace(writeField)) continue;

            if (!obj.TryGetPropertyValue(writeField!, out var node) || node is null)
            {
                projected[relApi] = null;
                continue;
            }

            if (rel.Write.Mode == RelationWriteMode.ById)
            {
                var targetId = node.ToJsonString().Trim('"');
                var target = await GetAsync(targetContract.ResourceKey, targetId, expand: null, ct);
                projected[relApi] = target;
            }
            else if (rel.Write.Mode == RelationWriteMode.ByIdList)
            {
                if (node is not JsonArray arr)
                {
                    projected[relApi] = new JsonArray();
                    continue;
                }

                var outArr = new JsonArray();
                foreach (var idNode in arr)
                {
                    if (idNode is null) continue;
                    var targetId = idNode.ToJsonString().Trim('"');
                    var target = await GetAsync(targetContract.ResourceKey, targetId, expand: null, ct);
                    if (target != null) outArr.Add(target);
                }
                projected[relApi] = outArr;
            }
        }
    }

    private static void ValidateBody(ResourceContract c, CrudOperation op, JsonObject body)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var oc = c.Operations[op];

        var allowed = new HashSet<string>(oc.InputShape, StringComparer.OrdinalIgnoreCase);

        foreach (var key in body.Select(kv => kv.Key))
        {
            if (!allowed.Contains(key))
                errors[key] = new[] { "Field is not allowed for this operation." };
        }

        if (op == CrudOperation.Create)
        {
            foreach (var req in oc.RequiredOnCreate)
            {
                if (!body.ContainsKey(req))
                    errors[req] = new[] { "Field is required." };
            }
        }

        if (op == CrudOperation.Update)
        {
            var concurrencyApiName = oc.Concurrency?.FieldApiName;
            foreach (var imm in oc.ImmutableFields)
            {
                if (body.ContainsKey(imm)
                    && !string.Equals(imm, concurrencyApiName, StringComparison.OrdinalIgnoreCase))
                    errors[imm] = new[] { "Field is immutable." };
            }

            if (oc.Concurrency is { RequiredOnUpdate: true } cc)
            {
                if (!body.ContainsKey(cc.FieldApiName))
                    errors[cc.FieldApiName] = new[] { "Concurrency token is required." };
            }
        }

        // Field-level validation (MinLength, MaxLength, Min, Max, Regex, AllowedValues)
        DataSurface.EFCore.Validation.FieldValidator.ValidateFieldConstraints(c, body, errors);

        if (errors.Count > 0)
            throw new CrudRequestValidationException(errors);
    }
}
