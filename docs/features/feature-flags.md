# Feature Flags

DataSurface allows selective enablement of features via `DataSurfaceFeatures`. This lets you use only the capabilities you need, reducing complexity and overhead.

---

## Presets

Three built-in presets cover common scenarios:

| Preset | Description | Use Case |
|--------|-------------|----------|
| `Minimal` | Core CRUD + validation only | Simple APIs, microservices, maximum performance |
| `Standard` | Core + security + observability | Most production applications **(default)** |
| `Full` | All features enabled | Feature-rich applications with webhooks |

```csharp
builder.Services.AddDataSurfaceEfCore(opt =>
{
    opt.Features = DataSurfaceFeatures.Minimal;
    // or
    opt.Features = DataSurfaceFeatures.Standard;
    // or
    opt.Features = DataSurfaceFeatures.Full;
});
```

---

## Individual Flags

Customize exactly which features are active:

```csharp
opt.Features = new DataSurfaceFeatures
{
    // Core CRUD
    EnableFieldValidation = true,
    EnableDefaultValues = true,
    EnableComputedFields = true,
    EnableFieldProjection = true,

    // Security
    EnableTenantIsolation = true,
    EnableRowLevelSecurity = true,
    EnableResourceAuthorization = true,
    EnableFieldAuthorization = true,

    // Observability
    EnableAuditLogging = true,
    EnableMetrics = false,          // Disable metrics
    EnableTracing = false,          // Disable tracing

    // Caching
    EnableQueryCaching = true,

    // Extensibility
    EnableHooks = true,
    EnableOverrides = true,

    // Integration
    EnableWebhooks = false          // Disable webhooks
};
```

---

## Flag Reference

| Category | Flag | Minimal | Standard | Full | Description |
|----------|------|---------|----------|------|-------------|
| **Core CRUD** | `EnableFieldValidation` | ✅ | ✅ | ✅ | MinLength, MaxLength, Min, Max, Regex, AllowedValues |
| | `EnableDefaultValues` | ✅ | ✅ | ✅ | Apply default values on create |
| | `EnableComputedFields` | ✅ | ✅ | ✅ | Evaluate computed expressions at read time |
| | `EnableFieldProjection` | ✅ | ✅ | ✅ | Support `?fields=` query parameter |
| **Security** | `EnableTenantIsolation` | ❌ | ✅ | ✅ | `[CrudTenant]` attribute support |
| | `EnableRowLevelSecurity` | ❌ | ✅ | ✅ | `IResourceFilter<T>` support |
| | `EnableResourceAuthorization` | ❌ | ✅ | ✅ | `IResourceAuthorizer<T>` support |
| | `EnableFieldAuthorization` | ❌ | ✅ | ✅ | `IFieldAuthorizer` support |
| **Observability** | `EnableAuditLogging` | ❌ | ✅ | ✅ | `IAuditLogger` integration |
| | `EnableMetrics` | ❌ | ✅ | ✅ | OpenTelemetry metrics |
| | `EnableTracing` | ❌ | ✅ | ✅ | Distributed tracing |
| **Caching** | `EnableQueryCaching` | ❌ | ✅ | ✅ | `IQueryResultCache` integration |
| **Extensibility** | `EnableHooks` | ❌ | ✅ | ✅ | Lifecycle hooks |
| | `EnableOverrides` | ❌ | ✅ | ✅ | CRUD operation overrides |
| **Integration** | `EnableWebhooks` | ❌ | ❌ | ✅ | Webhook publishing (opt-in) |

---

## Always-On Behavior

The following behaviors are **always enforced** regardless of feature flags:

- Unknown field rejection on create/update
- `RequiredOnCreate` validation
- `Immutable` field rejection on update
- Pagination enforcement
- Filter/sort allowlists
- Startup contract validation

---

## Related

- [Configuration](../getting-started/configuration.md) — Where feature flags fit in the configuration system
- [Configuration Options Reference](../reference/configuration-options.md) — All options classes
