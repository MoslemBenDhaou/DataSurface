using System.Collections.Concurrent;
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

    private readonly ConcurrentDictionary<string, (ResourceContract Contract, DateTime UpdatedAt)> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new dynamic contract provider.
    /// </summary>
    /// <param name="store">Store used to load dynamic entity definitions.</param>
    /// <param name="builder">Builder used to convert definitions into normalized contracts.</param>
    public DynamicResourceContractProvider(IDynamicEntityDefStore store, DynamicContractBuilder builder)
    {
        _store = store;
        _builder = builder;
    }

    /// <inheritdoc />
    public IReadOnlyList<ResourceContract> All
    {
        get
        {
            // Important: this is a sync property. We keep it simple:
            // - load everything once at startup (recommended) OR
            // - let the app call WarmUpAsync() at startup.
            // For correctness here, we return cached contracts only.
            return _cache.Values.Select(x => x.Contract).ToList();
        }
    }

    /// <summary>
    /// Loads all entity definitions from the store and builds contracts into the in-memory cache.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    public async Task WarmUpAsync(CancellationToken ct)
    {
        var defs = await _store.GetAllAsync(ct);
        foreach (var d in defs)
        {
            var rc = _builder.Build(d);
            _cache[d.EntityKey] = (rc, UpdatedAt: DateTime.UtcNow); // store time; invalidation uses DB timestamp per key
        }
    }

    /// <inheritdoc />
    public ResourceContract GetByResourceKey(string resourceKey)
    {
        // Check cache first to avoid blocking async call when possible
        if (_cache.TryGetValue(resourceKey, out var cached))
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
            throw new KeyNotFoundException($"Unknown dynamic resourceKey '{resourceKey}'.");

        // Return cached if still fresh
        if (_cache.TryGetValue(resourceKey, out var cached) && cached.UpdatedAt >= updatedAt.Value)
            return cached.Contract;

        // Rebuild: cache miss or stale
        var def = await _store.GetByEntityKeyAsync(resourceKey, ct)
                  ?? throw new KeyNotFoundException($"Unknown dynamic resourceKey '{resourceKey}'.");

        var rc2 = _builder.Build(def);
        _cache[resourceKey] = (rc2, updatedAt.Value);

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
