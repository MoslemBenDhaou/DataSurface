using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.EFCore.Contracts;

namespace DataSurface.EFCore.Caching;

/// <summary>
/// Provides caching for CRUD query results.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface to enable query result caching for read-heavy resources.
/// The default implementation uses <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>.
/// </para>
/// <code>
/// // Register distributed cache implementation
/// builder.Services.AddStackExchangeRedisCache(options =>
/// {
///     options.Configuration = "localhost:6379";
/// });
/// 
/// // Enable DataSurface caching
/// builder.Services.AddDataSurfaceCaching(options =>
/// {
///     options.EnableQueryCaching = true;
///     options.DefaultCacheDuration = TimeSpan.FromMinutes(10);
/// });
/// </code>
/// </remarks>
public interface IQueryResultCache
{
    /// <summary>
    /// Gets a cached list result.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="cacheKey">The cache key based on query parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cached result or null if not cached.</returns>
    Task<PagedResult<JsonObject>?> GetListAsync(string resourceKey, string cacheKey, CancellationToken ct = default);

    /// <summary>
    /// Sets a list result in the cache.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="cacheKey">The cache key based on query parameters.</param>
    /// <param name="result">The result to cache.</param>
    /// <param name="duration">Optional cache duration override.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetListAsync(string resourceKey, string cacheKey, PagedResult<JsonObject> result, TimeSpan? duration = null, CancellationToken ct = default);

    /// <summary>
    /// Gets a cached single entity result.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The entity ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cached result or null if not cached.</returns>
    Task<JsonObject?> GetAsync(string resourceKey, object id, CancellationToken ct = default);

    /// <summary>
    /// Sets a single entity result in the cache.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The entity ID.</param>
    /// <param name="result">The result to cache.</param>
    /// <param name="duration">Optional cache duration override.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetAsync(string resourceKey, object id, JsonObject result, TimeSpan? duration = null, CancellationToken ct = default);

    /// <summary>
    /// Invalidates all cached entries for a resource.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvalidateResourceAsync(string resourceKey, CancellationToken ct = default);

    /// <summary>
    /// Invalidates a specific cached entity.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="id">The entity ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvalidateAsync(string resourceKey, object id, CancellationToken ct = default);

    /// <summary>
    /// Generates a cache key for a list query.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="spec">The query specification.</param>
    /// <param name="expand">The expand specification.</param>
    /// <returns>A unique cache key.</returns>
    string GenerateListCacheKey(string resourceKey, QuerySpec spec, ExpandSpec? expand);
}
