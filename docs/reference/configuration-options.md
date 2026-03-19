# Configuration Options Reference

Complete property-by-property reference for all DataSurface options classes.

---

## DataSurfaceEfCoreOptions

Configured via `AddDataSurfaceEfCore()`. Controls EF Core backend behavior.

```csharp
builder.Services.AddDataSurfaceEfCore(opt =>
{
    // ... properties below
});
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AssembliesToScan` | `Assembly[]` | `[]` | Assemblies to scan for `[CrudResource]` classes |
| `AutoRegisterCrudEntities` | `bool` | `true` | Auto-register discovered entities in DbContext |
| `EnableSoftDeleteFilter` | `bool` | `true` | Apply `ISoftDelete` global query filter |
| `EnableRowVersionConvention` | `bool` | `true` | Configure `byte[]` RowVersion as EF concurrency token |
| `EnableTimestampConvention` | `bool` | `true` | Auto-populate `CreatedAt`/`UpdatedAt` for `ITimestamped` |
| `UseCamelCaseApiNames` | `bool` | `true` | Convert property names to camelCase for API names |
| `Features` | `DataSurfaceFeatures` | `Standard` | Feature flags — see [Feature Flags](../features/feature-flags.md) |
| `ContractBuilderOptions` | `ContractBuilderOptions` | *(see below)* | Fine-tune contract generation |

### ContractBuilderOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ExposeFieldsOnlyWhenAnnotated` | `bool` | `true` | Only expose properties with `[CrudField]` |

---

## DataSurfaceHttpOptions

Passed to `MapDataSurfaceCrud()`. Controls HTTP endpoint mapping and HTTP-level features.

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    // ... properties below
});
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ApiPrefix` | `string` | `"/api"` | Base route prefix for all endpoints |
| `MapStaticResources` | `bool` | `true` | Map routes for static EF Core resources |
| `MapDynamicCatchAll` | `bool` | `false` | Map `/api/d/{route}` for dynamic resources |
| `DynamicPrefix` | `string` | `"/d"` | Route prefix for dynamic resources |
| `MapResourceDiscoveryEndpoint` | `bool` | `true` | Enable `GET /api/$resources` |
| `RequireAuthorizationByDefault` | `bool` | `false` | Require auth on all endpoints |
| `DefaultPolicy` | `string?` | `null` | Default ASP.NET Core authorization policy |
| `EnableEtags` | `bool` | `true` | Include ETag headers in responses |
| `EnableConditionalGet` | `bool` | `false` | Support `If-None-Match` → 304 responses |
| `CacheControlMaxAgeSeconds` | `int?` | `null` | Set `Cache-Control: max-age=N` header |
| `ThrowOnRouteCollision` | `bool` | `false` | Fail startup on duplicate routes |
| `EnablePutForFullUpdate` | `bool` | `false` | Enable PUT endpoints for full replacement |
| `EnableImportExport` | `bool` | `false` | Enable import/export endpoints |
| `EnableRateLimiting` | `bool` | `false` | Enable ASP.NET Core rate limiting |
| `RateLimitingPolicy` | `string?` | `null` | Rate limiting policy name |
| `EnableApiKeyAuth` | `bool` | `false` | Enable API key authentication |
| `ApiKeyHeaderName` | `string` | `"X-Api-Key"` | Header name for API key |
| `EnableWebhooks` | `bool` | `false` | Enable webhook publishing |

---

## DataSurfaceDynamicOptions

Configured via `AddDataSurfaceDynamic()`. Controls dynamic entity behavior.

```csharp
builder.Services.AddDataSurfaceDynamic(opt =>
{
    // ... properties below
});
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Schema` | `string` | `"dbo"` | Database schema for dynamic metadata tables |
| `WarmUpContractsOnStart` | `bool` | `true` | Load all dynamic contracts at application startup |

---

## DataSurfaceAdminOptions

Passed to `MapDataSurfaceAdmin()`. Controls the admin REST API.

```csharp
app.MapDataSurfaceAdmin(new DataSurfaceAdminOptions
{
    // ... properties below
});
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Prefix` | `string` | `"/admin/ds"` | Route prefix for admin endpoints |
| `RequireAuthorization` | `bool` | `true` | Require auth on admin endpoints |
| `Policy` | `string?` | `null` | ASP.NET Core authorization policy for admin access |

---

## DataSurfaceFeatures

Configured via `DataSurfaceEfCoreOptions.Features`. See [Feature Flags](../features/feature-flags.md) for full documentation including presets and the flag reference table.

---

## DataSurfaceCacheOptions

Configured via `Configure<DataSurfaceCacheOptions>()`. Controls query result caching.

```csharp
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
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnableQueryCaching` | `bool` | `false` | Enable server-side query result caching |
| `DefaultCacheDuration` | `TimeSpan` | 5 minutes | Default cache duration for all resources |
| `ResourceConfigs` | `Dictionary<string, ResourceCacheConfig>` | `{}` | Per-resource cache configuration |

### ResourceCacheConfig

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Duration` | `TimeSpan` | *(uses default)* | Cache duration for this resource |
| `CacheList` | `bool` | `true` | Cache list query results |
| `CacheGet` | `bool` | `true` | Cache individual entity results |
