namespace DataSurface.EFCore.Caching;

/// <summary>
/// Options for configuring DataSurface caching behavior.
/// </summary>
public sealed class DataSurfaceCacheOptions
{
    /// <summary>
    /// Gets or sets whether query result caching is enabled.
    /// </summary>
    public bool EnableQueryCaching { get; set; } = false;

    /// <summary>
    /// Gets or sets the default cache duration for query results.
    /// </summary>
    public TimeSpan DefaultCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the cache key prefix.
    /// </summary>
    public string CacheKeyPrefix { get; set; } = "ds:";

    /// <summary>
    /// Gets or sets resource-specific cache configurations.
    /// </summary>
    public Dictionary<string, ResourceCacheConfig> ResourceConfigs { get; set; } = new();
}

/// <summary>
/// Cache configuration for a specific resource.
/// </summary>
public sealed class ResourceCacheConfig
{
    /// <summary>
    /// Gets or sets whether caching is enabled for this resource.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the cache duration for this resource.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Gets or sets whether to cache list operations.
    /// </summary>
    public bool CacheList { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to cache get operations.
    /// </summary>
    public bool CacheGet { get; set; } = false;
}
