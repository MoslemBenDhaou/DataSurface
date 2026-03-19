# Caching

DataSurface provides two caching layers: **query caching** (server-side result caching via `IDistributedCache`) and **response caching** (HTTP-level ETag and Cache-Control headers). Both are independent and can be used separately or together.

---

## Query Caching

Cache CRUD query results server-side using any `IDistributedCache` implementation (Redis, SQL Server, memory, etc.).

### Setup

```csharp
// Add a distributed cache implementation
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});

// Configure DataSurface caching
builder.Services.Configure<DataSurfaceCacheOptions>(options =>
{
    options.EnableQueryCaching = true;
    options.DefaultCacheDuration = TimeSpan.FromMinutes(5);
    options.ResourceConfigs["Product"] = new ResourceCacheConfig
    {
        Duration = TimeSpan.FromMinutes(30),
        CacheList = true,
        CacheGet = true
    };
});

// Register the cache implementation
builder.Services.AddSingleton<IQueryResultCache, DistributedQueryResultCache>();
```

### Behavior

| Operation | Caching |
|-----------|---------|
| **List** | Cached by resource key + full query spec (filters, sort, page) |
| **Get** | Cached by resource key + entity ID |
| **Create** | Invalidates all cached entries for the resource |
| **Update** | Invalidates the specific entity cache and list caches |
| **Delete** | Invalidates the specific entity cache and list caches |

### Per-Resource Configuration

Different resources can have different cache durations and strategies:

```csharp
options.ResourceConfigs["Product"] = new ResourceCacheConfig
{
    Duration = TimeSpan.FromMinutes(30),  // Long cache for rarely-changing data
    CacheList = true,
    CacheGet = true
};

options.ResourceConfigs["Order"] = new ResourceCacheConfig
{
    Duration = TimeSpan.FromSeconds(30),  // Short cache for frequently-changing data
    CacheList = true,
    CacheGet = false  // Don't cache individual orders
};
```

### Feature Flag

```csharp
opt.Features.EnableQueryCaching = true;  // default: true in Standard/Full
```

---

## Response Caching

HTTP-level caching using ETags and Cache-Control headers.

### ETag-Based Conditional GET

When ETags are enabled, GET responses include an `ETag` header:

```http
GET /api/users/1

HTTP/1.1 200 OK
ETag: W/"AAAAAAB="
```

Subsequent requests can include `If-None-Match` to get a `304 Not Modified` when data hasn't changed:

```http
GET /api/users/1
If-None-Match: W/"AAAAAAB="

HTTP/1.1 304 Not Modified
```

This saves bandwidth — the full response body is not sent.

### Cache-Control Headers

Configure `Cache-Control` headers for client-side and proxy caching:

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableConditionalGet = true,          // If-None-Match → 304
    CacheControlMaxAgeSeconds = 300       // Cache-Control: max-age=300
});
```

### Configuration

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableEtags = true,                   // Include ETag in responses (default: true)
    EnableConditionalGet = true,          // Support If-None-Match → 304
    CacheControlMaxAgeSeconds = 300       // Set Cache-Control header
});
```

---

## Compiled Queries

Pre-compiled EF Core queries for improved performance on common operations:

```csharp
builder.Services.AddSingleton<CompiledQueryCache>();

// Usage in custom code or overrides
var cache = sp.GetRequiredService<CompiledQueryCache>();
var findById = cache.GetOrCreateFindByIdQuery<User, int>("Id");
var user = findById(dbContext, 5);
```

Compiled queries avoid the overhead of query expression tree compilation on each request.

---

## Related

- [Concurrency](concurrency.md) — ETag-based concurrency control
- [CRUD Operations](crud-operations.md) — How cache invalidation integrates with writes
