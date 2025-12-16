using DataSurface.Admin.Dtos;
using DataSurface.Admin.Validation;
using DataSurface.Dynamic.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataSurface.Admin.Services;

/// <summary>
/// Provides CRUD operations for dynamic metadata (entity definitions, properties, relations).
/// </summary>
public sealed class DynamicMetadataAdminService
{
    private readonly DbContext _db;

    /// <summary>
    /// Creates a new instance of the admin service.
    /// </summary>
    /// <param name="db">The EF Core database context.</param>
    public DynamicMetadataAdminService(DbContext db) => _db = db;

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
    /// Creates or updates a dynamic entity definition.
    /// </summary>
    /// <param name="dto">The entity definition to upsert.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The saved entity definition and any validation errors.</returns>
    public async Task<(AdminEntityDefDto Entity, IDictionary<string, string[]> Errors)> UpsertEntityAsync(AdminEntityDefDto dto, CancellationToken ct)
    {
        var errors = DynamicMetadataValidator.Validate(dto);
        if (errors.Count > 0) return (dto, errors);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var existing = await _db.Set<DsEntityDefRow>()
            .Include(x => x.Properties)
            .Include(x => x.Relations)
            .FirstOrDefaultAsync(x => x.EntityKey == dto.EntityKey, ct);

        if (existing is null)
        {
            existing = new DsEntityDefRow { EntityKey = dto.EntityKey };
            _db.Add(existing);
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
        await tx.CommitAsync(ct);

        return (await GetEntityAsync(dto.EntityKey, ct) ?? dto, new Dictionary<string, string[]>());
    }

    /// <summary>
    /// Deletes a dynamic entity definition.
    /// </summary>
    /// <param name="entityKey">The entity key to delete.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><c>true</c> if the entity existed and was deleted; otherwise <c>false</c>.</returns>
    public async Task<bool> DeleteEntityAsync(string entityKey, CancellationToken ct)
    {
        var row = await _db.Set<DsEntityDefRow>()
            .Include(x => x.Properties)
            .Include(x => x.Relations)
            .FirstOrDefaultAsync(x => x.EntityKey == entityKey, ct);

        if (row is null) return false;

        _db.RemoveRange(row.Properties);
        _db.RemoveRange(row.Relations);
        _db.Remove(row);
        await _db.SaveChangesAsync(ct);
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
    /// Imports dynamic entity definitions from the provided payload.
    /// </summary>
    /// <param name="payload">The import payload.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of imported entities and any per-entity validation errors.</returns>
    public async Task<(int Imported, List<(string EntityKey, IDictionary<string, string[]>)> Errors)> ImportAsync(AdminImportPayloadDto payload, CancellationToken ct)
    {
        var errs = new List<(string, IDictionary<string, string[]>)>();
        var imported = 0;

        foreach (var e in payload.Entities)
        {
            var (saved, errors) = await UpsertEntityAsync(e, ct);
            if (errors.Count > 0) errs.Add((e.EntityKey, errors));
            else imported++;
        }

        return (imported, errs);
    }

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
