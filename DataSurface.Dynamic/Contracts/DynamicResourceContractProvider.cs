using DataSurface.Core;
using DataSurface.Core.Contracts;
using DataSurface.Dynamic.Stores;
using DataSurface.EFCore.Interfaces;

namespace DataSurface.Dynamic.Contracts;

/// <summary>
/// Contract provider that builds <see cref="ResourceContract"/> definitions from dynamic entity definitions
/// stored in an <see cref="IDynamicEntityDefStore"/>.
/// </summary>
public sealed class DynamicResourceContractProvider : IResourceContractProvider
{
    private readonly IDynamicEntityDefStore _store;
    private readonly DynamicContractBuilder _builder;

    // Shared (singleton) cache: the provider itself is scoped, so a per-instance cache would
    // always be empty for fresh scopes and `All` would never surface dynamic resources in
    // discovery/schema.
    private readonly DynamicContractCache _cache;

    /// <summary>
    /// Creates a new dynamic contract provider.
    /// </summary>
    /// <param name="store">Store used to load dynamic entity definitions.</param>
    /// <param name="builder">Builder used to convert definitions into normalized contracts.</param>
    /// <param name="cache">Shared application-wide contract cache.</param>
    public DynamicResourceContractProvider(IDynamicEntityDefStore store, DynamicContractBuilder builder, DynamicContractCache cache)
    {
        _store = store;
        _builder = builder;
        _cache = cache;
    }

    /// <inheritdoc />
    public IReadOnlyList<ResourceContract> All
    {
        get
        {
            // Sync property backed by the shared cache. The cache is populated at startup by
            // the warm-up hosted service (DataSurfaceDynamicOptions.WarmUpContractsOnStart)
            // and incrementally by every per-key resolution.
            return _cache.Entries.Values.Select(x => x.Contract).ToList();
        }
    }

    /// <summary>
    /// Loads all entity definitions from the store and builds contracts into the shared cache.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    public async Task WarmUpAsync(CancellationToken ct)
    {
        var defs = await _store.GetAllWithTimestampsAsync(ct);
        foreach (var (def, updatedAt) in defs)
        {
            var rc = _builder.Build(def);
            // Stamp with the definition ROW's UpdatedAt (not DateTime.UtcNow): freshness checks
            // compare against the DB timestamp, and a wall-clock stamp would mask updates that
            // raced the warm-up or differ by clock skew.
            _cache.Entries[def.EntityKey] = (rc, updatedAt);
        }
    }

    /// <inheritdoc />
    public ResourceContract GetByResourceKey(string resourceKey)
    {
        // Check cache first to avoid blocking async call when possible
        if (_cache.Entries.TryGetValue(resourceKey, out var cached))
            return cached.Contract;

        // Fallback: run async on thread pool to avoid deadlocks
        return Task.Run(() => GetByResourceKeyAsync(resourceKey, CancellationToken.None)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gets a resource contract by its resource key, rebuilding the contract if it has changed.
    /// </summary>
    /// <param name="resourceKey">The resource key to look up.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The resolved resource contract.</returns>
    public async Task<ResourceContract> GetByResourceKeyAsync(string resourceKey, CancellationToken ct)
    {
        var updatedAt = await _store.GetUpdatedAtAsync(resourceKey, ct);
        if (updatedAt is null)
        {
            // Definition was deleted: drop any stale cached contract so `All` stays accurate.
            _cache.Entries.TryRemove(resourceKey, out _);
            throw new KeyNotFoundException($"Unknown dynamic resourceKey '{resourceKey}'.");
        }

        // Return cached if still fresh
        if (_cache.Entries.TryGetValue(resourceKey, out var cached) && cached.UpdatedAt >= updatedAt.Value)
            return cached.Contract;

        // Rebuild: cache miss or stale
        var def = await _store.GetByEntityKeyAsync(resourceKey, ct)
                  ?? throw new KeyNotFoundException($"Unknown dynamic resourceKey '{resourceKey}'.");

        var rc2 = _builder.Build(def);
        _cache.Entries[def.EntityKey] = (rc2, updatedAt.Value);

        return rc2;
    }

    /// <summary>
    /// Attempts to resolve a contract by its route.
    /// </summary>
    /// <param name="route">The route to look up.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The contract if found; otherwise <c>null</c>.</returns>
    public async Task<ResourceContract?> TryGetByRouteAsync(string route, CancellationToken ct)
    {
        var def = await _store.GetByRouteAsync(route, ct);
        if (def is null) return null;

        var rc = await GetByResourceKeyAsync(def.EntityKey, ct);
        return rc;
    }
}
