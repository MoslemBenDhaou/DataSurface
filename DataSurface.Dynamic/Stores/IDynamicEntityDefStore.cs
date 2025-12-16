using DataSurface.Core.Contracts;

namespace DataSurface.Dynamic.Stores;

/// <summary>
/// Provides access to dynamic entity definitions used to build resource contracts at runtime.
/// </summary>
public interface IDynamicEntityDefStore
{
    /// <summary>
    /// Gets an entity definition by its entity key.
    /// </summary>
    /// <param name="entityKey">The entity key.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The entity definition if found; otherwise <c>null</c>.</returns>
    Task<EntityDef?> GetByEntityKeyAsync(string entityKey, CancellationToken ct);

    /// <summary>
    /// Gets an entity definition by its route.
    /// </summary>
    /// <param name="route">The route segment.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The entity definition if found; otherwise <c>null</c>.</returns>
    Task<EntityDef?> GetByRouteAsync(string route, CancellationToken ct);

    /// <summary>
    /// Gets all entity definitions.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A list of entity definitions.</returns>
    Task<IReadOnlyList<EntityDef>> GetAllAsync(CancellationToken ct);

    // Used for cache invalidation checks
    /// <summary>
    /// Gets the last updated timestamp for the specified entity definition.
    /// </summary>
    /// <param name="entityKey">The entity key.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The last updated timestamp if present; otherwise <c>null</c>.</returns>
    Task<DateTime?> GetUpdatedAtAsync(string entityKey, CancellationToken ct);
}
