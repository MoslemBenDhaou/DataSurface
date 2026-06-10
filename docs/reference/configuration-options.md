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
| `Features` | `DataSurfaceFeatures` | *(all flags enabled)* | Feature flags — see [Feature Flags](../features/feature-flags.md) |
| `AssembliesToScan` | `Assembly[]` | `[]` | Assemblies to scan for `[CrudResource]` classes |
| `AutoRegisterCrudEntities` | `bool` | `false` | Auto-register discovered resource types in the EF model |
| `EnableSoftDeleteFilter` | `bool` | `false` | Apply `ISoftDelete` global query filter |
| `EnableRowVersionConvention` | `bool` | `false` | Configure `byte[]` RowVersion as EF concurrency token |
| `EnableTimestampConvention` | `bool` | `false` | Auto-populate `CreatedAt`/`UpdatedAt` for `ITimestamped` |
| `UseCamelCaseApiNames` | `bool` | `true` | Convert property names to camelCase for API names |
| `StrictQuery` | `bool` | `false` | Reject filter/sort on non-allowlisted fields with HTTP 400 instead of silently ignoring them |
| `ContractBuilderOptions` | `ContractBuilderOptions` | *(see below)* | Fine-tune contract generation |

### ContractBuilderOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ExposeFieldsOnlyWhenAnnotated` | `bool` | `true` | Only expose properties with `[CrudField]` |
| `DefaultIncludeScalarsInRead` | `bool` | `false` | When `ExposeFieldsOnlyWhenAnnotated` is `false`, include un-annotated scalar properties in the Read shape |
| `UseCamelCaseApiNames` | `bool` | `true` | Generate camelCase API names from CLR property names (synced from `DataSurfaceEfCoreOptions.UseCamelCaseApiNames`) |

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
| `MapDynamicCatchAll` | `bool` | `false` | Map `/api/d/{route}` for dynamic resources (opt-in) |
| `DynamicPrefix` | `string` | `"/d"` | Route prefix for dynamic resources |
| `MapResourceDiscoveryEndpoint` | `bool` | `false` | Enable `GET /api/$resources` |
| `MapSchemaEndpoint` | `bool` | `true` | Map `GET /api/$schema/{resourceKey}`. Schema and discovery endpoints participate in the same default authorization / API key / rate limiting as CRUD endpoints |
| `RequireAuthorizationByDefault` | `bool` | `false` | Require auth on all endpoints |
| `DefaultPolicy` | `string?` | `null` | Default ASP.NET Core authorization policy |
| `EnableEtags` | `bool` | `false` | Include ETag headers in responses |
| `EnableConditionalGet` | `bool` | `false` | Support `If-None-Match` → 304 responses |
| `CacheControlMaxAgeSeconds` | `int` | `0` | `Cache-Control: private, max-age=N` for GET responses (0 disables the header) |
| `ThrowOnRouteCollision` | `bool` | `true` | Fail at mapping time on duplicate routes (a collision would otherwise surface as a 500 at request time) |
| `EnablePutForFullUpdate` | `bool` | `false` | Enable PUT endpoints for full replacement |
| `EnableBulkOperations` | `bool` | `false` | Map bulk endpoints (`POST /api/{resource}/bulk`) |
| `EnableStreaming` | `bool` | `false` | Map streaming endpoints (`GET /api/{resource}/stream`) |
| `EnableImportExport` | `bool` | `false` | Enable import/export endpoints |
| `MaxExportRows` | `int` | `100000` | Max rows an export will materialize; beyond it the response is truncated and `X-Export-Truncated: true` is returned |
| `EnableRateLimiting` | `bool` | `false` | Enable ASP.NET Core rate limiting |
| `RateLimitingPolicy` | `string?` | `null` | Rate limiting policy name |
| `EnableApiKeyAuth` | `bool` | `false` | Enable API key authentication |
| `ApiKeyHeaderName` | `string` | `"X-Api-Key"` | Header name for API key |

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
| `Schema` | `string` | `"dbo"` | Database schema for dynamic metadata tables |
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
        Enabled = true,
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
| `CacheKeyPrefix` | `string` | `"ds:"` | Prefix for generated cache keys |
| `ResourceConfigs` | `Dictionary<string, ResourceCacheConfig>` | `{}` | Per-resource cache configuration |

### ResourceCacheConfig

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `false` | Enable caching for this resource |
| `Duration` | `TimeSpan?` | `null` *(uses default)* | Cache duration for this resource |
| `CacheList` | `bool` | `false` | Cache list query results |
| `CacheGet` | `bool` | `false` | Cache individual entity results |
