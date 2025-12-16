using DataSurface.Core.Contracts;
using DataSurface.Dynamic.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataSurface.Dynamic.Stores;

/// <summary>
/// Entity Framework Core implementation of <see cref="IDynamicEntityDefStore"/>.
/// </summary>
public sealed class EfDynamicEntityDefStore : IDynamicEntityDefStore
{
    private readonly DbContext _db;

    /// <summary>
    /// Creates a new store.
    /// </summary>
    /// <param name="db">The EF Core database context.</param>
    public EfDynamicEntityDefStore(DbContext db) => _db = db;

    /// <inheritdoc />
    /// <summary>
    /// Retrieves an entity definition by its entity key.
    /// </summary>
    /// <param name="entityKey">The entity key.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The entity definition, or null if not found.</returns>
    public async Task<EntityDef?> GetByEntityKeyAsync(string entityKey, CancellationToken ct)
    {
        var row = await _db.Set<DsEntityDefRow>()
            .AsNoTracking()
            .Include(x => x.Properties)
            .Include(x => x.Relations)
            .FirstOrDefaultAsync(x => x.EntityKey == entityKey, ct);

        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    /// <summary>
    /// Retrieves an entity definition by its route.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The entity definition, or null if not found.</returns>
    public async Task<EntityDef?> GetByRouteAsync(string route, CancellationToken ct)
    {
        var row = await _db.Set<DsEntityDefRow>()
            .AsNoTracking()
            .Include(x => x.Properties)
            .Include(x => x.Relations)
            .FirstOrDefaultAsync(x => x.Route == route, ct);

        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    /// <summary>
    /// Retrieves all entity definitions.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A list of entity definitions.</returns>
    public async Task<IReadOnlyList<EntityDef>> GetAllAsync(CancellationToken ct)
    {
        var rows = await _db.Set<DsEntityDefRow>()
            .AsNoTracking()
            .Include(x => x.Properties)
            .Include(x => x.Relations)
            .ToListAsync(ct);

        return rows.Select(Map).ToList();
    }

    /// <inheritdoc />
    /// <summary>
    /// Retrieves the last updated date and time for an entity definition.
    /// </summary>
    /// <param name="entityKey">The entity key.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The last updated date and time, or null if not found.</returns>
    public async Task<DateTime?> GetUpdatedAtAsync(string entityKey, CancellationToken ct)
    {
        return await _db.Set<DsEntityDefRow>()
            .AsNoTracking()
            .Where(x => x.EntityKey == entityKey)
            .Select(x => (DateTime?)x.UpdatedAt)
            .FirstOrDefaultAsync(ct);
    }

    private static EntityDef Map(DsEntityDefRow e)
    {
        var props = e.Properties.Select(p => new PropertyDef(
            Name: p.Name,
            ApiName: p.ApiName,
            Type: p.Type,
            Nullable: p.Nullable,
            In: p.InFlags,
            RequiredOnCreate: p.RequiredOnCreate,
            Immutable: p.Immutable,
            Hidden: p.Hidden,
            MinLength: p.MinLength,
            MaxLength: p.MaxLength,
            Min: p.Min,
            Max: p.Max,
            Regex: p.Regex,
            ConcurrencyToken: p.ConcurrencyToken,
            ConcurrencyMode: p.ConcurrencyMode,
            ConcurrencyRequiredOnUpdate: p.ConcurrencyRequiredOnUpdate
        )).ToList();

        var rels = e.Relations.Select(r => new RelationDef(
            Name: r.Name,
            ApiName: r.ApiName,
            Kind: r.Kind,
            TargetResourceKey: r.TargetEntityKey,
            ExpandAllowed: r.ExpandAllowed,
            DefaultExpanded: r.DefaultExpanded,
            WriteMode: r.WriteMode,
            WriteFieldName: r.WriteFieldName,
            RequiredOnCreate: r.RequiredOnCreate,
            ForeignKeyProperty: r.ForeignKeyProperty
        )).ToList();

        return new EntityDef(
            EntityKey: e.EntityKey,
            Route: e.Route,
            Backend: e.Backend,
            KeyName: e.KeyName,
            KeyType: e.KeyType,
            MaxPageSize: e.MaxPageSize,
            MaxExpandDepth: e.MaxExpandDepth,
            EnableList: e.EnableList,
            EnableGet: e.EnableGet,
            EnableCreate: e.EnableCreate,
            EnableUpdate: e.EnableUpdate,
            EnableDelete: e.EnableDelete,
            Properties: props,
            Relations: rels,
            Policies: null
        );
    }
}
