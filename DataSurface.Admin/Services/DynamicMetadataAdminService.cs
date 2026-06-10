using System.Text.Json;
using DataSurface.Admin.Dtos;
using DataSurface.Admin.Validation;
using DataSurface.Dynamic.Entities;
using DataSurface.EFCore.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DataSurface.Admin.Services;

/// <summary>
/// Provides CRUD operations for dynamic metadata (entity definitions, properties, relations).
/// </summary>
public sealed class DynamicMetadataAdminService
{
    private readonly DbContext _db;
    private readonly DynamicIndexRebuildService _reindex;
    private readonly ILogger<DynamicMetadataAdminService>? _logger;
    private readonly StaticResourceContractProvider? _staticContracts;

    /// <summary>
    /// Creates a new instance of the admin service.
    /// </summary>
    /// <param name="db">The EF Core database context.</param>
    /// <param name="reindex">Service used to rebuild dynamic indexes after schema changes.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="staticContracts">Optional static contract provider used to reject entity
    /// keys / routes that collide with statically defined resources.</param>
    public DynamicMetadataAdminService(
        DbContext db,
        DynamicIndexRebuildService reindex,
        ILogger<DynamicMetadataAdminService>? logger = null,
        StaticResourceContractProvider? staticContracts = null)
    {
        _db = db;
        _reindex = reindex;
        _logger = logger;
        _staticContracts = staticContracts;
    }

    /// <summary>
    /// Lists all dynamic entity definitions.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A list of entity definition DTOs.</returns>
    public async Task<List<AdminEntityDefDto>> ListEntitiesAsync(CancellationToken ct)
    {
        var rows = await _db.Set<DsEntityDefRow>()
            .AsNoTracking()
            .Include(x => x.Properties)
            .Include(x => x.Relations)
            .OrderBy(x => x.EntityKey)
            .ToListAsync(ct);

        return rows.Select(MapToDto).ToList();
    }

    /// <summary>
    /// Gets a single dynamic entity definition by entity key.
    /// </summary>
    /// <param name="entityKey">The entity key to look up.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The entity definition if found; otherwise <c>null</c>.</returns>
    public async Task<AdminEntityDefDto?> GetEntityAsync(string entityKey, CancellationToken ct)
    {
        var row = await _db.Set<DsEntityDefRow>()
            .AsNoTracking()
            .Include(x => x.Properties)
            .Include(x => x.Relations)
            .FirstOrDefaultAsync(x => x.EntityKey == entityKey, ct);

        return row is null ? null : MapToDto(row);
    }

    /// <summary>
    /// Creates or updates a dynamic entity definition. When an update changes property
    /// definitions, the entity's filter/sort indexes are rebuilt automatically (stale index rows
    /// would otherwise silently return wrong results).
    /// </summary>
    /// <param name="dto">The entity definition to upsert.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The saved entity definition and any validation errors.</returns>
    public async Task<(AdminEntityDefDto Entity, IDictionary<string, string[]> Errors)> UpsertEntityAsync(AdminEntityDefDto dto, CancellationToken ct)
    {
        var errors = DynamicMetadataValidator.Validate(dto, _staticContracts);
        if (errors.Count > 0) return (dto, errors);

        await using var tx = await BeginTransactionIfSupportedAsync(ct);

        var needsReindex = await UpsertCoreAsync(dto, ct);

        if (tx is not null) await tx.CommitAsync(ct);

        if (needsReindex)
        {
            _logger?.LogInformation("Property definitions of dynamic entity '{EntityKey}' changed; rebuilding indexes.", dto.EntityKey);
            await _reindex.RebuildEntityAsync(dto.EntityKey, ct);
        }

        return (await GetEntityAsync(dto.EntityKey, ct) ?? dto, new Dictionary<string, string[]>());
    }

    /// <summary>
    /// Deletes a dynamic entity definition, INCLUDING all of its stored records and index rows,
    /// so data cannot be resurrected by re-creating the same entity key later.
    /// </summary>
    /// <param name="entityKey">The entity key to delete.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><c>true</c> if the entity existed and was deleted; otherwise <c>false</c>.</returns>
    public async Task<bool> DeleteEntityAsync(string entityKey, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfSupportedAsync(ct);

        var row = await _db.Set<DsEntityDefRow>()
            .Include(x => x.Properties)
            .Include(x => x.Relations)
            .FirstOrDefaultAsync(x => x.EntityKey == entityKey, ct);

        if (row is null) return false;

        // Purge the data alongside the metadata in the same SaveChanges/transaction.
        var records = await _db.Set<DsDynamicRecordRow>()
            .Where(r => r.EntityKey == entityKey)
            .ToListAsync(ct);
        var indexRows = await _db.Set<DsDynamicIndexRow>()
            .Where(r => r.EntityKey == entityKey)
            .ToListAsync(ct);

        _db.RemoveRange(records);
        _db.RemoveRange(indexRows);
        _db.RemoveRange(row.Properties);
        _db.RemoveRange(row.Relations);
        _db.Remove(row);
        await _db.SaveChangesAsync(ct);

        if (tx is not null) await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Exports all dynamic entity definitions.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The export payload.</returns>
    public async Task<AdminExportPayloadDto> ExportAsync(CancellationToken ct)
    {
        var entities = await ListEntitiesAsync(ct);
        return new AdminExportPayloadDto { Entities = entities };
    }

    /// <summary>
    /// Imports dynamic entity definitions from the provided payload with atomic semantics:
    /// all entities are validated first (any validation error aborts the import before applying
    /// anything), then applied inside a single transaction when the provider supports one.
    /// Per-entity apply failures are collected into the error list instead of aborting with a 500;
    /// when a transaction is active, any failure rolls the whole import back.
    /// </summary>
    /// <param name="payload">The import payload.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of imported entities and any per-entity errors.</returns>
    public async Task<(int Imported, List<(string EntityKey, IDictionary<string, string[]>)> Errors)> ImportAsync(AdminImportPayloadDto payload, CancellationToken ct)
    {
        var errs = new List<(string, IDictionary<string, string[]>)>();

        // Phase 1: validate everything before applying anything.
        foreach (var e in payload.Entities)
        {
            var errors = DynamicMetadataValidator.Validate(e, _staticContracts);
            if (errors.Count > 0) errs.Add((e.EntityKey, errors));
        }
        if (errs.Count > 0) return (0, errs);

        // Phase 2: apply.
        await using var tx = await BeginTransactionIfSupportedAsync(ct);

        var imported = 0;
        var reindexKeys = new List<string>();

        foreach (var e in payload.Entities)
        {
            try
            {
                var needsReindex = await UpsertCoreAsync(e, ct);
                if (needsReindex) reindexKeys.Add(e.EntityKey);
                imported++;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Import of dynamic entity '{EntityKey}' failed.", e.EntityKey);
                errs.Add((e.EntityKey, new Dictionary<string, string[]>
                {
                    ["import"] = new[] { ex.Message }
                }));
                // Drop any partially staged changes so they cannot leak into the next entity's
                // SaveChanges (relevant for providers without transactions, e.g. InMemory).
                _db.ChangeTracker.Clear();
            }
        }

        if (errs.Count > 0 && tx is not null)
        {
            await tx.RollbackAsync(ct);
            return (0, errs);
        }

        if (tx is not null) await tx.CommitAsync(ct);

        foreach (var key in reindexKeys)
        {
            try
            {
                _logger?.LogInformation("Property definitions of dynamic entity '{EntityKey}' changed during import; rebuilding indexes.", key);
                await _reindex.RebuildEntityAsync(key, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Index rebuild after import failed for dynamic entity '{EntityKey}'.", key);
            }
        }

        return (imported, errs);
    }

    // Core upsert without validation/transaction handling (shared by Upsert and Import).
    // Returns true when this was an UPDATE that changed property definitions, i.e. the
    // existing index rows are stale and a rebuild is required.
    private async Task<bool> UpsertCoreAsync(AdminEntityDefDto dto, CancellationToken ct)
    {
        var existing = await _db.Set<DsEntityDefRow>()
            .Include(x => x.Properties)
            .Include(x => x.Relations)
            .FirstOrDefaultAsync(x => x.EntityKey == dto.EntityKey, ct);

        var isUpdate = existing is not null;
        var oldPropertySignature = existing is null
            ? null
            : existing.Properties.Select(PropertySignature).OrderBy(x => x, StringComparer.Ordinal).ToList();

        if (existing is null)
        {
            existing = new DsEntityDefRow { EntityKey = dto.EntityKey };
            _db.Add(existing);
        }
        else if (dto.UpdatedAt is { } clientVersion)
        {
            // Optimistic concurrency: make EF compare against the version the client last read,
            // so two stale edits cannot silently overwrite each other (throws on conflict).
            _db.Entry(existing).Property(x => x.UpdatedAt).OriginalValue = clientVersion;
        }

        Apply(dto, existing);

        // Replace children (simple + correct; optimize later with diff)
        _db.RemoveRange(existing.Properties);
        _db.RemoveRange(existing.Relations);

        existing.Properties = dto.Properties.Select(p => new DsPropertyDefRow
        {
            Name = p.Name,
            ApiName = p.ApiName,
            Type = p.Type,
            Nullable = p.Nullable,
            InFlags = p.InFlags,
            RequiredOnCreate = p.RequiredOnCreate,
            Immutable = p.Immutable,
            Hidden = p.Hidden,
            Indexed = p.Indexed,
            MinLength = p.MinLength,
            MaxLength = p.MaxLength,
            Min = p.Min,
            Max = p.Max,
            Regex = p.Regex,
            ConcurrencyToken = p.ConcurrencyToken,
            ConcurrencyMode = p.ConcurrencyMode,
            ConcurrencyRequiredOnUpdate = p.ConcurrencyRequiredOnUpdate,
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        existing.Relations = dto.Relations.Select(r => new DsRelationDefRow
        {
            Name = r.Name,
            ApiName = r.ApiName,
            Kind = r.Kind,
            TargetEntityKey = r.TargetEntityKey,
            ExpandAllowed = r.ExpandAllowed,
            DefaultExpanded = r.DefaultExpanded,
            WriteMode = r.WriteMode,
            WriteFieldName = r.WriteFieldName,
            RequiredOnCreate = r.RequiredOnCreate,
            ForeignKeyProperty = r.ForeignKeyProperty,
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        if (!isUpdate || oldPropertySignature is null) return false;

        var newPropertySignature = dto.Properties.Select(PropertySignature).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return !oldPropertySignature.SequenceEqual(newPropertySignature, StringComparer.Ordinal);
    }

    // Signature of the property facets that affect index rows (apiName, type and flags).
    private static string PropertySignature(DsPropertyDefRow p)
        => $"{p.ApiName}|{p.Type}|{(int)p.InFlags}|{p.Hidden}|{p.Indexed}";

    private static string PropertySignature(AdminPropertyDefDto p)
        => $"{p.ApiName}|{p.Type}|{(int)p.InFlags}|{p.Hidden}|{p.Indexed}";

    // Explicit relational transactions throw on non-relational providers (EF InMemory);
    // fall back to single-SaveChanges atomicity there.
    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(CancellationToken ct)
        => _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

    private static void Apply(AdminEntityDefDto dto, DsEntityDefRow row)
    {
        row.Route = dto.Route;
        row.Backend = dto.Backend;
        row.KeyName = dto.KeyName;
        row.KeyType = dto.KeyType;
        row.MaxPageSize = dto.MaxPageSize;
        row.MaxExpandDepth = dto.MaxExpandDepth;

        row.EnableList = dto.EnableList;
        row.EnableGet = dto.EnableGet;
        row.EnableCreate = dto.EnableCreate;
        row.EnableUpdate = dto.EnableUpdate;
        row.EnableDelete = dto.EnableDelete;

        row.TenantFieldName = dto.TenantFieldName;
        row.TenantFieldApiName = dto.TenantFieldApiName;
        row.TenantClaimType = dto.TenantClaimType;
        row.TenantRequired = dto.TenantRequired;
        row.PoliciesJson = dto.Policies is { Count: > 0 } ? JsonSerializer.Serialize(dto.Policies) : null;
    }

    private static AdminEntityDefDto MapToDto(DsEntityDefRow row)
    {
        return new AdminEntityDefDto
        {
            Id = row.Id,
            EntityKey = row.EntityKey,
            Route = row.Route,
            Backend = row.Backend,
            KeyName = row.KeyName,
            KeyType = row.KeyType,
            MaxPageSize = row.MaxPageSize,
            MaxExpandDepth = row.MaxExpandDepth,
            EnableList = row.EnableList,
            EnableGet = row.EnableGet,
            EnableCreate = row.EnableCreate,
            EnableUpdate = row.EnableUpdate,
            EnableDelete = row.EnableDelete,
            TenantFieldName = row.TenantFieldName,
            TenantFieldApiName = row.TenantFieldApiName,
            TenantClaimType = row.TenantClaimType,
            TenantRequired = row.TenantRequired,
            Policies = string.IsNullOrWhiteSpace(row.PoliciesJson)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(row.PoliciesJson),
            UpdatedAt = row.UpdatedAt,
            Properties = row.Properties.OrderBy(p => p.ApiName).Select(p => new AdminPropertyDefDto
            {
                Id = p.Id,
                Name = p.Name,
                ApiName = p.ApiName,
                Type = p.Type,
                Nullable = p.Nullable,
                InFlags = p.InFlags,
                RequiredOnCreate = p.RequiredOnCreate,
                Immutable = p.Immutable,
                Hidden = p.Hidden,
                Indexed = p.Indexed,
                MinLength = p.MinLength,
                MaxLength = p.MaxLength,
                Min = p.Min,
                Max = p.Max,
                Regex = p.Regex,
                ConcurrencyToken = p.ConcurrencyToken,
                ConcurrencyMode = p.ConcurrencyMode,
                ConcurrencyRequiredOnUpdate = p.ConcurrencyRequiredOnUpdate
            }).ToList(),
            Relations = row.Relations.OrderBy(r => r.ApiName).Select(r => new AdminRelationDefDto
            {
                Id = r.Id,
                Name = r.Name,
                ApiName = r.ApiName,
                Kind = r.Kind,
                TargetEntityKey = r.TargetEntityKey,
                ExpandAllowed = r.ExpandAllowed,
                DefaultExpanded = r.DefaultExpanded,
                WriteMode = r.WriteMode,
                WriteFieldName = r.WriteFieldName,
                RequiredOnCreate = r.RequiredOnCreate,
                ForeignKeyProperty = r.ForeignKeyProperty
            }).ToList()
        };
    }
}
