using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace DataSurface.EFCore.Queries;

/// <summary>
/// Caches and manages compiled EF Core queries for improved performance.
/// </summary>
/// <remarks>
/// <para>
/// Compiled queries avoid the overhead of expression tree compilation on each execution,
/// providing significant performance improvements for frequently executed queries.
/// </para>
/// <para>
/// This cache automatically creates and stores compiled queries for common CRUD operations.
/// </para>
/// </remarks>
public sealed class CompiledQueryCache
{
    private readonly ConcurrentDictionary<string, object> _findByIdQueries = new();
    private readonly ConcurrentDictionary<string, object> _countQueries = new();
    private readonly ConcurrentDictionary<string, object> _existsQueries = new();

    /// <summary>
    /// Gets or creates a compiled query for finding an entity by ID.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="keyPropertyName">The name of the key property.</param>
    /// <returns>A compiled query delegate.</returns>
    public Func<DbContext, TKey, TEntity?> GetOrCreateFindByIdQuery<TEntity, TKey>(string keyPropertyName)
        where TEntity : class
    {
        var cacheKey = $"{typeof(TEntity).FullName}:{keyPropertyName}:{typeof(TKey).Name}";

        return (Func<DbContext, TKey, TEntity?>)_findByIdQueries.GetOrAdd(cacheKey, _ =>
            EF.CompileQuery((DbContext db, TKey id) =>
                db.Set<TEntity>().FirstOrDefault(e => EF.Property<TKey>(e, keyPropertyName)!.Equals(id))));
    }

    /// <summary>
    /// Gets or creates a compiled query for counting entities.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>A compiled query delegate.</returns>
    public Func<DbContext, int> GetOrCreateCountQuery<TEntity>()
        where TEntity : class
    {
        var cacheKey = typeof(TEntity).FullName!;

        return (Func<DbContext, int>)_countQueries.GetOrAdd(cacheKey, _ =>
            EF.CompileQuery((DbContext db) => db.Set<TEntity>().Count()));
    }

    /// <summary>
    /// Gets or creates a compiled query for checking entity existence by ID.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="keyPropertyName">The name of the key property.</param>
    /// <returns>A compiled query delegate.</returns>
    public Func<DbContext, TKey, bool> GetOrCreateExistsQuery<TEntity, TKey>(string keyPropertyName)
        where TEntity : class
    {
        var cacheKey = $"{typeof(TEntity).FullName}:{keyPropertyName}:{typeof(TKey).Name}:exists";

        return (Func<DbContext, TKey, bool>)_existsQueries.GetOrAdd(cacheKey, _ =>
            EF.CompileQuery((DbContext db, TKey id) =>
                db.Set<TEntity>().Any(e => EF.Property<TKey>(e, keyPropertyName)!.Equals(id))));
    }

    /// <summary>
    /// Gets statistics about the compiled query cache.
    /// </summary>
    /// <returns>Cache statistics.</returns>
    public CompiledQueryCacheStats GetStats()
    {
        return new CompiledQueryCacheStats
        {
            FindByIdQueryCount = _findByIdQueries.Count,
            CountQueryCount = _countQueries.Count,
            ExistsQueryCount = _existsQueries.Count,
            TotalQueryCount = _findByIdQueries.Count + _countQueries.Count + _existsQueries.Count
        };
    }

    /// <summary>
    /// Clears all cached compiled queries.
    /// </summary>
    public void Clear()
    {
        _findByIdQueries.Clear();
        _countQueries.Clear();
        _existsQueries.Clear();
    }
}

/// <summary>
/// Statistics about the compiled query cache.
/// </summary>
public sealed record CompiledQueryCacheStats
{
    /// <summary>
    /// Gets the number of cached find-by-id queries.
    /// </summary>
    public int FindByIdQueryCount { get; init; }

    /// <summary>
    /// Gets the number of cached count queries.
    /// </summary>
    public int CountQueryCount { get; init; }

    /// <summary>
    /// Gets the number of cached exists queries.
    /// </summary>
    public int ExistsQueryCount { get; init; }

    /// <summary>
    /// Gets the total number of cached queries.
    /// </summary>
    public int TotalQueryCount { get; init; }
}
