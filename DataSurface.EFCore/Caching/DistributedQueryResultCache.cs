using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.EFCore.Contracts;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace DataSurface.EFCore.Caching;

/// <summary>
/// Distributed cache implementation for query results using <see cref="IDistributedCache"/>.
/// </summary>
public sealed class DistributedQueryResultCache : IQueryResultCache
{
    private readonly IDistributedCache _cache;
    private readonly DataSurfaceCacheOptions _options;

    /// <summary>
    /// Creates a new distributed cache instance.
    /// </summary>
    /// <param name="cache">The distributed cache implementation.</param>
    /// <param name="options">Cache options.</param>
    public DistributedQueryResultCache(IDistributedCache cache, IOptions<DataSurfaceCacheOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<PagedResult<JsonObject>?> GetListAsync(string resourceKey, string cacheKey, CancellationToken ct = default)
    {
        if (!IsListCachingEnabled(resourceKey)) return null;

        var key = BuildKey(resourceKey, "list", cacheKey);
        var data = await _cache.GetStringAsync(key, ct);
        if (data is null) return null;

        try
        {
            var entry = JsonSerializer.Deserialize<CachedListEntry>(data);
            if (entry?.Data is null) return null;

            // The entry stamps the resource list-version it was written under. Read the current
            // version AFTER fetching the entry and compare. If the version key is ABSENT (never
            // set, or evicted) treat every entry as stale — entries are only ever written with a
            // freshly-initialized random version, so nothing can match a missing namespace. This
            // closes the eviction window (no "0"-default entry can be resurrected).
            var version = await _cache.GetStringAsync(ListVersionKey(resourceKey), ct);
            if (version is null) return null;
            return entry.Version == version ? entry.Data : null;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetListAsync(string resourceKey, string cacheKey, PagedResult<JsonObject> result, TimeSpan? duration = null, string? observedVersion = null, CancellationToken ct = default)
    {
        if (!IsListCachingEnabled(resourceKey)) return;

        var version = await GetOrInitListVersionAsync(resourceKey, ct);
        // Stale-fill guard: if the caller observed a version before its DB read and it has since been
        // bumped by a concurrent write, the result we're about to cache is already stale — skip it.
        if (observedVersion is not null && observedVersion != version) return;

        var key = BuildKey(resourceKey, "list", cacheKey);
        var data = JsonSerializer.Serialize(new CachedListEntry { Version = version, Data = result });
        var expiry = GetDuration(resourceKey, duration);

        await _cache.SetStringAsync(key, data, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry
        }, ct);
    }

    /// <inheritdoc />
    public async Task<JsonObject?> GetAsync(string resourceKey, object id, CancellationToken ct = default)
    {
        if (!IsGetCachingEnabled(resourceKey)) return null;

        var key = BuildKey(resourceKey, "get", id.ToString()!);
        var data = await _cache.GetStringAsync(key, ct);
        
        if (data is null) return null;

        try
        {
            return JsonNode.Parse(data)?.AsObject();
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(string resourceKey, object id, JsonObject result, TimeSpan? duration = null, CancellationToken ct = default)
    {
        if (!IsGetCachingEnabled(resourceKey)) return;

        var key = BuildKey(resourceKey, "get", id.ToString()!);
        var data = result.ToJsonString();
        var expiry = GetDuration(resourceKey, duration);

        await _cache.SetStringAsync(key, data, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry
        }, ct);
    }

    /// <inheritdoc />
    public async Task InvalidateResourceAsync(string resourceKey, CancellationToken ct = default)
    {
        // Bump the per-resource list version so every previously cached list key becomes
        // unreachable — across all processes sharing the distributed cache, not just this one.
        // Stale entries then lapse by their own TTL. (Per-id get entries are invalidated by
        // InvalidateAsync, whose key is deterministic and already cross-process.)
        await _cache.SetStringAsync(ListVersionKey(resourceKey), Guid.NewGuid().ToString("N"),
            new DistributedCacheEntryOptions(), ct);
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(string resourceKey, object id, CancellationToken ct = default)
    {
        var key = BuildKey(resourceKey, "get", id.ToString()!);
        await _cache.RemoveAsync(key, ct);
    }

    /// <inheritdoc />
    public string GenerateListCacheKey(string resourceKey, QuerySpec spec, ExpandSpec? expand)
    {
        var sb = new StringBuilder();
        sb.Append($"p{spec.Page}s{spec.PageSize}");
        
        if (!string.IsNullOrEmpty(spec.Sort))
            sb.Append($"o{spec.Sort}");

        if (spec.Filters?.Count > 0)
        {
            foreach (var (field, value) in spec.Filters.OrderBy(f => f.Key))
            {
                sb.Append($"f{field}:{value}");
            }
        }

        if (!string.IsNullOrEmpty(spec.Search))
            sb.Append($"q{spec.Search}");

        if (!string.IsNullOrEmpty(spec.Fields))
            sb.Append($"fl{spec.Fields}");

        if (expand?.Expand?.Any() == true)
            sb.Append($"e{string.Join(",", expand.Expand.OrderBy(x => x))}");

        // Hash for shorter keys
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
        return hash.ToLowerInvariant();
    }

    private string BuildKey(string resourceKey, string type, string suffix)
        => $"{_options.CacheKeyPrefix}{resourceKey}:{type}:{suffix}";

    private string ListVersionKey(string resourceKey)
        => $"{_options.CacheKeyPrefix}{resourceKey}:listver";

    /// <inheritdoc />
    public Task<string> GetListVersionAsync(string resourceKey, CancellationToken ct = default)
        => GetOrInitListVersionAsync(resourceKey, ct);

    // Returns the current list-cache version for a resource, initializing it to a fresh random value
    // when absent. Used on WRITE so entries are never stamped with a guessable/default version that a
    // reader could match after the version key is evicted.
    private async Task<string> GetOrInitListVersionAsync(string resourceKey, CancellationToken ct)
    {
        var key = ListVersionKey(resourceKey);
        var existing = await _cache.GetStringAsync(key, ct);
        if (existing is not null) return existing;

        var fresh = Guid.NewGuid().ToString("N");
        await _cache.SetStringAsync(key, fresh, new DistributedCacheEntryOptions(), ct);
        return fresh;
    }

    private TimeSpan GetDuration(string resourceKey, TimeSpan? @override)
    {
        if (@override.HasValue) return @override.Value;

        if (_options.ResourceConfigs.TryGetValue(resourceKey, out var config) && config.Duration.HasValue)
            return config.Duration.Value;

        return _options.DefaultCacheDuration;
    }

    private bool IsListCachingEnabled(string resourceKey)
    {
        if (!_options.EnableQueryCaching) return false;
        if (!_options.ResourceConfigs.TryGetValue(resourceKey, out var config)) return true;
        return config.Enabled && config.CacheList;
    }

    private bool IsGetCachingEnabled(string resourceKey)
    {
        if (!_options.EnableQueryCaching) return false;
        if (!_options.ResourceConfigs.TryGetValue(resourceKey, out var config)) return true;
        return config.Enabled && config.CacheGet;
    }

    // Cached list payload stamped with the resource list-version it was written under, so a reader
    // can detect invalidation (a version bump) regardless of process or version-key eviction.
    private sealed class CachedListEntry
    {
        public string Version { get; set; } = "0";
        public PagedResult<JsonObject>? Data { get; set; }
    }
}
