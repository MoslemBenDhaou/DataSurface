# Configuration

DataSurface is configured through four options classes and a feature flags system. This page provides an overview of each configuration surface. For the complete property reference, see [Configuration Options Reference](../reference/configuration-options.md).

---

## EF Core Options — `DataSurfaceEfCoreOptions`

Controls how DataSurface discovers and manages EF Core–backed resources.

```csharp
builder.Services.AddDataSurfaceEfCore(opt =>
{
    opt.AssembliesToScan = [typeof(Program).Assembly];
    opt.AutoRegisterCrudEntities = true;
    opt.EnableSoftDeleteFilter = true;
    opt.EnableRowVersionConvention = true;
    opt.EnableTimestampConvention = true;
    opt.UseCamelCaseApiNames = true;
    opt.Features = DataSurfaceFeatures.Standard;
});
```

Key settings:
- **`AssembliesToScan`** — Which assemblies to scan for `[CrudResource]` classes
- **`Features`** — Enable/disable individual features via presets or flags (see [Feature Flags](../features/feature-flags.md))
- **Convention flags** — Automatic soft delete, row version, and timestamp handling

---

## HTTP Options — `DataSurfaceHttpOptions`

Controls how REST endpoints are mapped and which HTTP-level features are active.

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    ApiPrefix = "/api",
    MapStaticResources = true,
    MapDynamicCatchAll = true,
    EnableEtags = true,
    EnablePutForFullUpdate = false,
    EnableImportExport = false,
    EnableRateLimiting = false,
    EnableApiKeyAuth = false,
    EnableWebhooks = false
});
```

Key settings:
- **`ApiPrefix`** — Base route prefix for all endpoints
- **`MapDynamicCatchAll`** — Whether to enable `/api/d/{route}` for dynamic resources
- **Feature toggles** — PUT, import/export, rate limiting, API keys, webhooks

---

## Dynamic Options — `DataSurfaceDynamicOptions`

Controls runtime-defined entity behavior.

```csharp
builder.Services.AddDataSurfaceDynamic(opt =>
{
    opt.Schema = "dbo";
    opt.WarmUpContractsOnStart = true;
});
```

---

## Admin Options — `DataSurfaceAdminOptions`

Controls the admin REST API for managing dynamic entity definitions.

```csharp
app.MapDataSurfaceAdmin(new DataSurfaceAdminOptions
{
    Prefix = "/admin/ds",
    RequireAuthorization = true,
    Policy = "DataSurfaceAdmin"
});
```

---

## Feature Flags

Selectively enable or disable DataSurface capabilities using `DataSurfaceFeatures`. Three presets are available:

| Preset | Description |
|--------|-------------|
| `Minimal` | Core CRUD + validation only |
| `Standard` | Core + security + observability (default) |
| `Full` | All features including webhooks |

```csharp
opt.Features = DataSurfaceFeatures.Standard;
```

Or customize individual flags:

```csharp
opt.Features = new DataSurfaceFeatures
{
    EnableFieldValidation = true,
    EnableTenantIsolation = true,
    EnableAuditLogging = true,
    EnableMetrics = false,
    EnableWebhooks = false
};
```

Full details: [Feature Flags](../features/feature-flags.md)

---

## Next Steps

- [Architecture Overview](../architecture/overview.md) — Understand how the configuration fits into the system
- [Configuration Options Reference](../reference/configuration-options.md) — Complete property-by-property reference
