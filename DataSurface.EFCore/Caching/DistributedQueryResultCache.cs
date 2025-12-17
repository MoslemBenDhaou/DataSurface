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
            return JsonSerializer.Deserialize<PagedResult<JsonObject>>(data);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetListAsync(string resourceKey, string cacheKey, PagedResult<JsonObject> result, TimeSpan? duration = null, CancellationToken ct = default)
    {
        if (!IsListCachingEnabled(resourceKey)) return;

        var key = BuildKey(resourceKey, "list", cacheKey);
        var data = JsonSerializer.Serialize(result);
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
        // Note: IDistributedCache doesn't support pattern-based deletion
        // For full support, use Redis with SCAN or implement a key registry
        // This is a best-effort implementation that clears known keys
        var prefix = $"{_options.CacheKeyPrefix}{resourceKey}:";
        
        // In a real implementation, you would track keys or use Redis SCAN
        // For now, we rely on TTL expiration
        await Task.CompletedTask;
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

        if (expand?.Expand?.Any() == true)
            sb.Append($"e{string.Join(",", expand.Expand.OrderBy(x => x))}");

        // Hash for shorter keys
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
        return hash.ToLowerInvariant();
    }

    private string BuildKey(string resourceKey, string type, string suffix)
        => $"{_options.CacheKeyPrefix}{resourceKey}:{type}:{suffix}";

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
}
