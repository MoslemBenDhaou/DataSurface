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
    EnableApiKeyAuth = false
});
```

Key settings:
- **`ApiPrefix`** — Base route prefix for all endpoints
- **`MapDynamicCatchAll`** — Whether to enable `/api/d/{route}` for dynamic resources
- **Feature toggles** — PUT, import/export, rate limiting, API keys

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

Feature flags are an **AND-gate kill-switch**: a feature runs only when its flag is enabled **and** its wiring (service/attribute) is present. Flags **default to enabled**, so registering the dependency is enough — set a flag to `false` to force a feature off even when wired. Presets are opt-in lockdowns:

| Preset | What it permits |
|--------|-----------------|
| *(default)* | Every feature (wire what you need) |
| `Standard` | Everything except webhooks |
| `Minimal` | Only field validation + default values |

```csharp
opt.Features = DataSurfaceFeatures.Minimal; // lock down; omit for "permit everything"
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
