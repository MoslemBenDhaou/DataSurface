using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using DataSurface.Core;
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
    private static readonly HashSet<string> KnownOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "eq", "neq", "gt", "gte", "lt", "lte", "contains", "starts", "ends", "in", "isnull"
    };

    private readonly DbContext _db;
    private readonly DynamicResourceContractProvider _contracts;
    private readonly IDynamicIndexService _index;
    private readonly IServiceProvider _sp;

    private readonly CrudHookDispatcher _globalHooks;
    private readonly CrudResourceHookDispatcher _resourceHooks;
    private readonly CrudOverrideRegistry _overrides;
    private readonly ILogger<DynamicDataSurfaceCrudService> _logger;

    private readonly IResourceContractProvider _compositeContracts; // for expand targets
    private readonly CrudSecurityDispatcher? _security;
    private readonly DataSurfaceFeatures _features;

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
    /// <param name="security">Optional security dispatcher for authorization, redaction and audit logging.</param>
    /// <param name="features">Optional feature flags; a feature runs only when its flag is enabled and its wiring is present.</param>
    public DynamicDataSurfaceCrudService(
        DbContext db,
        DynamicResourceContractProvider contracts,
        IResourceContractProvider compositeContracts,
        IDynamicIndexService index,
        IServiceProvider sp,
        CrudHookDispatcher globalHooks,
        CrudResourceHookDispatcher resourceHooks,
        CrudOverrideRegistry overrides,
        ILogger<DynamicDataSurfaceCrudService> logger,
        CrudSecurityDispatcher? security = null,
        DataSurfaceFeatures? features = null)
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
        _security = security;
        _features = features ?? new DataSurfaceFeatures();
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

        if (_features.EnableOverrides && _overrides.TryGet<ListOverride>(c.ResourceKey, CrudOperation.List, out var ov))
        {
            var result = await ov!(c, spec, expand, svcCtx, ct);
            await _globalHooks.AfterGlobalAsync(hookCtx);
            return result;
        }

        var baseQuery = _db.Set<DsDynamicRecordRow>()
            .AsNoTracking()
            .Where(r => r.EntityKey == c.ResourceKey && !r.IsDeleted);

        // Tenant isolation
        if (_features.EnableTenantIsolation && c.Tenant is not null)
        {
            var tenantValue = ResolveTenantValue(c);
            baseQuery = baseQuery.Where(r => r.TenantValue == tenantValue);
        }

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

            // Resource-level authorization runs per item (same as Get); denied items are
            // excluded from the page instead of failing the whole list, mirroring how
            // LoadExpandTargetsAsync skips unauthorized expanded items.
            if (_security is not null)
            {
                try
                {
                    await _security.AuthorizeResourceAsync(c, obj, typeof(JsonObject), CrudOperation.List, ct);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
            }

            if (expand is not null) await ApplyExpandAsync(obj, row, c, expand, ct);
            // JSON read hook
            await _resourceHooks.AfterReadAsync(c.ResourceKey, row.Id, obj, hookCtx);
            items.Add(obj);
        }

        // Field-level authorization: redact unauthorized fields from the list results.
        _security?.RedactUnauthorizedFields(c, items);

        await _globalHooks.AfterGlobalAsync(hookCtx);

        if (_security is not null)
            await _security.LogAuditAsync(_security.CreateAuditEntry(CrudOperation.List, resourceKey), ct);

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

        if (_features.EnableOverrides && _overrides.TryGet<GetOverride>(c.ResourceKey, CrudOperation.Get, out var ov))
        {
            var result = await ov!(c, id, expand, svcCtx, ct);
            await _globalHooks.AfterGlobalAsync(hookCtx);
            return result;
        }

        var idStr = NormalizeIdString(c, id);
        var getQuery = _db.Set<DsDynamicRecordRow>()
            .AsNoTracking()
            .Where(r => r.EntityKey == c.ResourceKey && r.Id == idStr && !r.IsDeleted);
        if (_features.EnableTenantIsolation && c.Tenant is not null)
        {
            var tenantValue = ResolveTenantValue(c);
            getQuery = getQuery.Where(r => r.TenantValue == tenantValue);
        }
        var row = await getQuery.FirstOrDefaultAsync(ct);

        if (row is null)
        {
            await _globalHooks.AfterGlobalAsync(hookCtx);
            return null;
        }

        var obj = ProjectRowToJson(row, c);

        // Resource-level authorization
        if (_security is not null)
            await _security.AuthorizeResourceAsync(c, obj, typeof(JsonObject), CrudOperation.Get, ct);

        if (expand is not null) await ApplyExpandAsync(obj, row, c, expand, ct);

        // Field-level authorization (redact unauthorized fields)
        _security?.RedactUnauthorizedFields(c, obj);

        await _resourceHooks.AfterReadAsync(c.ResourceKey, row.Id, obj, hookCtx);
        await _globalHooks.AfterGlobalAsync(hookCtx);

        if (_security is not null)
            await _security.LogAuditAsync(_security.CreateAuditEntry(CrudOperation.Get, resourceKey, id.ToString()), ct);

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

        ValidateBody(c, CrudOperation.Create, body, _features.EnableFieldValidation);

        // Field-level write authorization
        _security?.ValidateFieldWriteAuthorization(c, body, CrudOperation.Create);

        var hookCtx = NewHookCtx(c, CrudOperation.Create);
        var svcCtx = NewSvcCtx();

        await _globalHooks.BeforeGlobalAsync(hookCtx);

        if (_features.EnableOverrides && _overrides.TryGet<CreateOverride>(c.ResourceKey, CrudOperation.Create, out var ov))
        {
            var result = await ov!(c, body, svcCtx, ct);
            await _globalHooks.AfterGlobalAsync(hookCtx);
            return result;
        }

        await _resourceHooks.BeforeCreateAsync(c.ResourceKey, body, hookCtx);

        // Determine record Id
        var keyApi = GetKeyApiName(c);
        var recordId = ResolveOrGenerateId(c, body);

        // Reject id collisions explicitly (400) instead of surfacing a raw DbUpdateException
        // (500). Soft-deleted rows are included: the (EntityKey, Id) primary key still exists.
        var duplicate = await _db.Set<DsDynamicRecordRow>()
            .AsNoTracking()
            .AnyAsync(r => r.EntityKey == c.ResourceKey && r.Id == recordId, ct);
        if (duplicate)
            throw new CrudRequestValidationException(new Dictionary<string, string[]>
            {
                [keyApi] = new[] { $"A record with id '{recordId}' already exists for resource '{c.ResourceKey}'." }
            });

        // Build stored JSON (only allowed Create fields)
        var stored = BuildStoredJson(c, CrudOperation.Create, body);
        stored[keyApi] = CreateKeyNode(c, keyApi, recordId);

        // Tenant isolation: resolve the current tenant and stamp it on the record (server-set,
        // overriding any client value) so records are filtered by tenant at the storage layer.
        var tenantValue = ResolveTenantValue(c);
        if (c.Tenant is not null && tenantValue is not null)
            stored[c.Tenant.FieldApiName] = tenantValue;

        var row = new DsDynamicRecordRow
        {
            EntityKey = c.ResourceKey,
            Id = recordId,
            DataJson = stored.ToJsonString(),
            TenantValue = tenantValue,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Add(row);

        // Stage index rows on the same change tracker so record + index rows are persisted
        // atomically by a single SaveChanges.
        await _index.StageIndexRowsAsync(c.ResourceKey, row.Id, c, stored, ct);
        await _db.SaveChangesAsync(ct);

        var created = ProjectRowToJson(row, c);
        await _resourceHooks.AfterCreateAsync(c.ResourceKey, created, hookCtx);

        await _globalHooks.AfterGlobalAsync(hookCtx);

        if (_security is not null)
            await _security.LogAuditAsync(_security.CreateAuditEntry(CrudOperation.Create, resourceKey, recordId, changes: body), ct);

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

        ValidateBody(c, CrudOperation.Update, patch, _features.EnableFieldValidation);

        // Field-level write authorization
        _security?.ValidateFieldWriteAuthorization(c, patch, CrudOperation.Update);

        var hookCtx = NewHookCtx(c, CrudOperation.Update);
        var svcCtx = NewSvcCtx();

        await _globalHooks.BeforeGlobalAsync(hookCtx);

        if (_features.EnableOverrides && _overrides.TryGet<UpdateOverride>(c.ResourceKey, CrudOperation.Update, out var ov))
        {
            var result = await ov!(c, id, patch, svcCtx, ct);
            await _globalHooks.AfterGlobalAsync(hookCtx);
            return result;
        }

        var idStr = NormalizeIdString(c, id);

        // Load tracked entity for concurrency
        var updQuery = _db.Set<DsDynamicRecordRow>()
            .Where(r => r.EntityKey == c.ResourceKey && r.Id == idStr && !r.IsDeleted);
        if (_features.EnableTenantIsolation && c.Tenant is not null)
        {
            var tenantValue = ResolveTenantValue(c);
            updQuery = updQuery.Where(r => r.TenantValue == tenantValue);
        }
        var row = await updQuery.FirstOrDefaultAsync(ct);

        if (row is null) throw new CrudNotFoundException(resourceKey, id);

        // Resource-level authorization
        if (_security is not null)
            await _security.AuthorizeResourceAsync(c, ProjectRowToJson(row, c), typeof(JsonObject), CrudOperation.Update, ct);

        await _resourceHooks.BeforeUpdateAsync(c.ResourceKey, id, patch, hookCtx);

        // Concurrency (RowVersion): set the EF original value so the conflict is enforced by
        // the database at SaveChanges (DbUpdateConcurrencyException -> 409).
        ApplyConcurrencyTokenIfAny(c, patch, row);

        var current = JsonNode.Parse(row.DataJson)?.AsObject() ?? new JsonObject();
        var keyApi = GetKeyApiName(c);

        // Apply patch only for allowed fields
        var stored = BuildStoredJson(c, CrudOperation.Update, patch, current);
        stored[keyApi] = CreateKeyNode(c, keyApi, row.Id); // keep key stable

        row.DataJson = stored.ToJsonString();
        row.UpdatedAt = DateTime.UtcNow;

        // Stage index rows on the same change tracker: record + index rows persist atomically.
        await _index.StageIndexRowsAsync(c.ResourceKey, row.Id, c, stored, ct);
        await _db.SaveChangesAsync(ct);

        var updated = ProjectRowToJson(row, c);
        await _resourceHooks.AfterUpdateAsync(c.ResourceKey, id, updated, hookCtx);

        await _globalHooks.AfterGlobalAsync(hookCtx);

        if (_security is not null)
            await _security.LogAuditAsync(_security.CreateAuditEntry(CrudOperation.Update, resourceKey, id.ToString(), changes: patch), ct);

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

        if (_features.EnableOverrides && _overrides.TryGet<DeleteOverride>(c.ResourceKey, CrudOperation.Delete, out var ov))
        {
            await ov!(c, id, deleteSpec, svcCtx, ct);
            await _globalHooks.AfterGlobalAsync(hookCtx);
            return;
        }

        var hard = deleteSpec?.HardDelete ?? false;

        var idStr = NormalizeIdString(c, id);
        var delQuery = _db.Set<DsDynamicRecordRow>()
            .Where(r => r.EntityKey == c.ResourceKey && r.Id == idStr);
        // Hard delete may target soft-deleted rows too, so a soft-deleted id can be purged.
        if (!hard)
            delQuery = delQuery.Where(r => !r.IsDeleted);
        if (_features.EnableTenantIsolation && c.Tenant is not null)
        {
            var tenantValue = ResolveTenantValue(c);
            delQuery = delQuery.Where(r => r.TenantValue == tenantValue);
        }
        var row = await delQuery.FirstOrDefaultAsync(ct);

        if (row is null) throw new CrudNotFoundException(resourceKey, id);

        // Resource-level authorization
        if (_security is not null)
            await _security.AuthorizeResourceAsync(c, ProjectRowToJson(row, c), typeof(JsonObject), CrudOperation.Delete, ct);

        await _resourceHooks.BeforeDeleteAsync(c.ResourceKey, id, hookCtx);

        // Honor the optimistic-concurrency token with the same semantics as Update.
        if (deleteSpec?.ConcurrencyToken is { } token)
        {
            var cc = c.Operations.TryGetValue(CrudOperation.Update, out var updOc) ? updOc.Concurrency : null;
            if (cc is not null && cc.Mode == ConcurrencyMode.RowVersion)
                EnforceRowVersion(cc, token, row);
        }

        if (!hard)
        {
            row.IsDeleted = true;
            row.UpdatedAt = DateTime.UtcNow;

            // Remove the index rows in the same SaveChanges so a soft-deleted record can no
            // longer be matched by filters/sorts.
            await _index.StageIndexRowsAsync(c.ResourceKey, row.Id, c, null, ct);
            await _db.SaveChangesAsync(ct);

            await _resourceHooks.AfterDeleteAsync(c.ResourceKey, id, hookCtx);
            await _globalHooks.AfterGlobalAsync(hookCtx);

            if (_security is not null)
                await _security.LogAuditAsync(_security.CreateAuditEntry(CrudOperation.Delete, resourceKey, id.ToString()), ct);

            _logger.LogInformation("Dynamic Soft-deleted {Resource} id={Id} in {ElapsedMs}ms", resourceKey, id, sw.ElapsedMilliseconds);
            return;
        }

        // hard delete: remove record and indexes in one SaveChanges
        await _index.StageIndexRowsAsync(c.ResourceKey, row.Id, c, null, ct);
        _db.Remove(row);
        await _db.SaveChangesAsync(ct);

        await _resourceHooks.AfterDeleteAsync(c.ResourceKey, id, hookCtx);
        await _globalHooks.AfterGlobalAsync(hookCtx);

        if (_security is not null)
            await _security.LogAuditAsync(_security.CreateAuditEntry(CrudOperation.Delete, resourceKey, id.ToString()), ct);

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
            throw new CrudOperationDisabledException(c.ResourceKey, op.ToString());
    }

    // Resolves the current tenant value for a tenant-scoped resource, reusing the EF security
    // dispatcher's claim resolution. Throws when a required tenant cannot be resolved.
    private string? ResolveTenantValue(ResourceContract c)
    {
        if (!_features.EnableTenantIsolation || c.Tenant is null) return null;

        var value = _security?.GetTenantId(c.Tenant);

        if (value is null && c.Tenant.Required)
            throw new UnauthorizedAccessException(
                $"Tenant claim '{c.Tenant.ClaimType}' is required but was not found.");

        return value;
    }

    // Canonicalizes a route/client id to the single storage format. Guid ids always use the
    // "D" (hyphenated) format, regardless of how the client supplied them ("N", braces, ...),
    // so generated and parsed ids compare equal.
    private static string NormalizeIdString(ResourceContract c, object id)
    {
        if (id is Guid g) return g.ToString("D");

        var s = id as string ?? Convert.ToString(id, CultureInfo.InvariantCulture) ?? id.ToString()!;

        if (c.Key.Type == FieldType.Guid && Guid.TryParse(s, out var parsed))
            return parsed.ToString("D");

        return s;
    }

    private static string GetKeyApiName(ResourceContract c)
    {
        var keyField = c.Fields.FirstOrDefault(f => f.Name.Equals(c.Key.Name, StringComparison.OrdinalIgnoreCase));
        return keyField?.ApiName ?? c.Key.Name;
    }

    // Extracts the raw string value from a JSON node without retaining JSON escaping
    // (ToJsonString().Trim('"') keeps escapes: "Café" would round-trip as "Café").
    private static string ExtractString(JsonNode node)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            if (v.TryGetValue<Guid>(out var g)) return g.ToString("D");
            if (v.TryGetValue<long>(out var l)) return l.ToString(CultureInfo.InvariantCulture);
            if (v.TryGetValue<decimal>(out var d)) return d.ToString(CultureInfo.InvariantCulture);
            if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        }
        return node.ToJsonString().Trim('"');
    }

    // Case-insensitive property lookup against a client-supplied JSON object.
    private static JsonNode? GetPropertyCI(JsonObject obj, string name)
    {
        if (obj.TryGetPropertyValue(name, out var v)) return v;
        foreach (var kv in obj)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    // Writes the key as a typed JSON value matching the contract key type so typed keys
    // round-trip (Int32/Int64 -> number, Guid/String -> string).
    private static JsonNode CreateKeyNode(ResourceContract c, string keyApi, string idStr)
    {
        switch (c.Key.Type)
        {
            case FieldType.Int32:
                if (int.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    return JsonValue.Create(i);
                break;
            case FieldType.Int64:
                if (long.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    return JsonValue.Create(l);
                break;
            default:
                return JsonValue.Create(idStr)!;
        }

        throw new CrudRequestValidationException(new Dictionary<string, string[]>
        {
            [keyApi] = new[] { $"'{idStr}' is not a valid {c.Key.Type} key value." }
        });
    }

    private string ResolveOrGenerateId(ResourceContract c, JsonObject body)
    {
        var keyApi = GetKeyApiName(c);

        var supplied = GetPropertyCI(body, keyApi);
        if (supplied is not null)
            return NormalizeIdString(c, ExtractString(supplied));

        // If not supplied, only auto-generate for Guid keys
        if (c.Key.Type == FieldType.Guid)
            return Guid.NewGuid().ToString("D");

        throw new CrudRequestValidationException(new Dictionary<string, string[]>
        {
            [keyApi] = new[] { "Key is required for this entity (no auto-generation configured)." }
        });
    }

    private JsonObject ProjectRowToJson(DsDynamicRecordRow row, ResourceContract contract, string? projectedFields = null)
    {
        var obj = JsonNode.Parse(row.DataJson)?.AsObject() ?? new JsonObject();

        // Surface the row-version concurrency token (base64) so clients/ETags can obtain it.
        var cc = contract.Operations.TryGetValue(CrudOperation.Update, out var updOc) ? updOc.Concurrency : null;
        if (cc is not null && cc.Mode == ConcurrencyMode.RowVersion && row.RowVersion is not null)
            obj[cc.FieldApiName] = Convert.ToBase64String(row.RowVersion);

        return ProjectJsonToReadShape(contract, obj, projectedFields);
    }

    private JsonObject ProjectJsonToReadShape(ResourceContract c, JsonObject json, string? projectedFields = null)
    {
        var o = new JsonObject();
        var readFields = c.Fields.Where(f => f.InRead && !f.Hidden);

        // Apply field projection if specified (kill-switched by EnableFieldProjection)
        if (_features.EnableFieldProjection && !string.IsNullOrWhiteSpace(projectedFields))
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

        // Validation is case-insensitive but storage must use the CANONICAL contract apiName:
        // map each client key to the contract's casing so reads/indexing find the values.
        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in c.Operations[op].InputShape)
            canonical[name] = name;
        foreach (var f in c.Fields)
            if (canonical.ContainsKey(f.ApiName))
                canonical[f.ApiName] = f.ApiName;

        foreach (var kv in input)
        {
            if (!canonical.TryGetValue(kv.Key, out var canonicalName)) continue;
            stored[canonicalName] = kv.Value?.DeepClone();
        }

        return stored;
    }

    private void ApplyConcurrencyTokenIfAny(ResourceContract c, JsonObject patch, DsDynamicRecordRow row)
    {
        var cc = c.Operations[CrudOperation.Update].Concurrency;
        if (cc is null || cc.Mode == ConcurrencyMode.None) return;

        var tokenNode = GetPropertyCI(patch, cc.FieldApiName);
        if (tokenNode is null)
        {
            if (cc.RequiredOnUpdate)
                throw new CrudRequestValidationException(new Dictionary<string, string[]>
                {
                    [cc.FieldApiName] = new[] { "Concurrency token is required." }
                });
            return;
        }

        if (cc.Mode == ConcurrencyMode.RowVersion)
            EnforceRowVersion(cc, ExtractString(tokenNode), row);
    }

    // Sets the EF original value for the row version so the concurrency conflict is enforced
    // by the provider at SaveChanges (DbUpdateConcurrencyException -> 409) instead of a racy
    // read-then-compare. Providers without generated row versions (e.g. InMemory) leave
    // RowVersion null; enforcement is skipped in that case.
    private void EnforceRowVersion(ConcurrencyContract cc, string tokenStr, DsDynamicRecordRow row)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(tokenStr);
        }
        catch (FormatException)
        {
            throw new CrudRequestValidationException(new Dictionary<string, string[]>
            {
                [cc.FieldApiName] = new[] { "Concurrency token is not a valid base64 value." }
            });
        }

        if (row.RowVersion is null) return; // provider without rowversion support

        _db.Entry(row).Property(r => r.RowVersion).OriginalValue = bytes;
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
        // Parse via DateTimeOffset and normalize to UTC so comparisons match the UTC values
        // the index service stores, regardless of server timezone.
        if (!DateTimeOffset.TryParse(val, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dto))
            throw new CrudRequestValidationException(new Dictionary<string, string[]>
            {
                ["filter"] = new[] { $"Invalid date filter value '{val}'." }
            });

        var d = dto.UtcDateTime;

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

    // Only a known operator prefix is treated as an operator; anything else is an equality
    // value. Without the whitelist, plain values containing ':' (ISO timestamps, URNs,
    // "ns:key" strings) would be torn apart at the first colon.
    private static (string op, string value) ParseOp(string raw)
    {
        var idx = raw.IndexOf(':');
        if (idx <= 0) return ("eq", raw.Trim());

        var prefix = raw[..idx].Trim();
        if (!KnownOps.Contains(prefix)) return ("eq", raw.Trim());

        return (prefix.ToLowerInvariant(), raw[(idx + 1)..].Trim());
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
                var targetId = ExtractString(node);
                var targets = await LoadExpandTargetsAsync(targetContract, new[] { targetId }, ct);
                projected[relApi] = targets.TryGetValue(targetId, out var t) ? t.DeepClone() : null;
            }
            else if (rel.Write.Mode == RelationWriteMode.ByIdList)
            {
                if (node is not JsonArray arr)
                {
                    projected[relApi] = new JsonArray();
                    continue;
                }

                // Batch-load all related ids in one query instead of one Get per id (fixes N+1),
                // preserving tenant isolation + resource auth + field redaction on the targets.
                var ids = arr.Where(x => x is not null).Select(x => ExtractString(x!)).ToList();
                var targets = await LoadExpandTargetsAsync(targetContract, ids, ct);

                var outArr = new JsonArray();
                foreach (var id in ids)
                    if (targets.TryGetValue(id, out var t))
                        outArr.Add(t.DeepClone());
                projected[relApi] = outArr;
            }
        }
    }

    // Batch-loads dynamic expand targets by id in a single query, projecting each to its read shape
    // and applying the SAME security as a direct Get: tenant isolation (in the query predicate),
    // resource-level authorization (denied targets are excluded), and field redaction. (The
    // per-item AfterRead hook / audit / Get overrides are intentionally not run for
    // included/expanded items.)
    private async Task<Dictionary<string, JsonObject>> LoadExpandTargetsAsync(
        ResourceContract targetContract, IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        var map = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (ids.Count == 0) return map;

        var idList = ids.Distinct(StringComparer.Ordinal).ToList();

        var query = _db.Set<DsDynamicRecordRow>().AsNoTracking()
            .Where(r => r.EntityKey == targetContract.ResourceKey && idList.Contains(r.Id) && !r.IsDeleted);

        if (_features.EnableTenantIsolation && targetContract.Tenant is not null)
        {
            var tenantValue = ResolveTenantValue(targetContract);
            query = query.Where(r => r.TenantValue == tenantValue);
        }

        var rows = await query.ToListAsync(ct);
        foreach (var r in rows)
        {
            var o = ProjectRowToJson(r, targetContract);

            if (_security is not null)
            {
                try
                {
                    await _security.AuthorizeResourceAsync(targetContract, o, typeof(JsonObject), CrudOperation.Get, ct);
                }
                catch (UnauthorizedAccessException)
                {
                    continue; // exclude denied expand targets instead of failing the parent read
                }
            }
            _security?.RedactUnauthorizedFields(targetContract, o);

            map[r.Id] = o;
        }

        return map;
    }

    private static void ValidateBody(ResourceContract c, CrudOperation op, JsonObject body, bool validateFieldConstraints)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var oc = c.Operations[op];

        var allowed = new HashSet<string>(oc.InputShape, StringComparer.OrdinalIgnoreCase);
        // The optimistic-concurrency token is valid on update even though it is not part of the
        // writable update shape; allow it through (it is consumed by the concurrency check).
        var concurrencyApiName = op == CrudOperation.Update ? oc.Concurrency?.FieldApiName : null;

        foreach (var key in body.Select(kv => kv.Key))
        {
            if (!allowed.Contains(key)
                && !string.Equals(key, concurrencyApiName, StringComparison.OrdinalIgnoreCase))
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
        if (validateFieldConstraints)
            DataSurface.EFCore.Validation.FieldValidator.ValidateFieldConstraints(c, body, errors);

        if (errors.Count > 0)
            throw new CrudRequestValidationException(errors);
    }
}
