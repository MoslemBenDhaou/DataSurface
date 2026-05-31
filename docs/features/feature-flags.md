# Feature Flags

`DataSurfaceFeatures` controls which DataSurface capabilities are active. Flags are an **AND-gate kill-switch**, not an on-switch:

> A feature runs only when **its flag is enabled AND its wiring is present** (the service is registered / the attribute is applied). Flags default to **enabled**, so registering the dependency is all you need to turn a feature on. Setting a flag to `false` **force-disables** the feature even when it is fully wired.

So you never have to "enable both": wire the feature as usual, and reach for a flag only when you want to *turn something off* — for example, disabling webhooks in one environment without changing DI, or locking a service down to a minimal surface.

The default (`new DataSurfaceFeatures()`, used when you don't set `opt.Features`) **permits every feature**. The `Minimal`/`Standard` presets are opt-in **lockdowns** that switch features off.

---

## Presets

| Preset | What it permits |
|--------|-----------------|
| *(default)* | **Every** feature — wire what you need, nothing is killed |
| `Full` | Every feature (same as the default) |
| `Standard` | Everything **except webhooks** (validation, defaults, computed, projection, all security, observability, caching, hooks, overrides) |
| `Minimal` | **Only** field validation + default values; everything else killed |

```csharp
builder.Services.AddDataSurfaceEfCore(opt =>
{
    // Default: every feature permitted. Lock down with a preset if you want less:
    opt.Features = DataSurfaceFeatures.Minimal;
    // opt.Features = DataSurfaceFeatures.Standard;
});
```

---

## Individual Flags

Start from the default (all permitted) and turn specific features off:

```csharp
opt.Features = new DataSurfaceFeatures
{
    EnableMetrics = false,   // kill metrics even if a recorder is registered
    EnableTracing = false,   // kill tracing
    EnableWebhooks = false   // kill webhooks even if an IWebhookPublisher is registered
};
```

Every flag you don't set stays at its default of `true` (permitted). Remember the AND-gate: a permitted feature still only runs when its dependency is wired.

---

## Flag Reference

✅ = permitted by that preset, ❌ = killed by that preset. A permitted feature still requires its wiring (the service in the description / the relevant attribute) to actually run.

| Category | Flag | Minimal | Standard | Full | Runs when permitted **and**… |
|----------|------|---------|----------|------|------------------------------|
| **Core CRUD** | `EnableFieldValidation` | ✅ | ✅ | ✅ | the contract declares constraints (MinLength, MaxLength, Min, Max, Regex, AllowedValues) |
| | `EnableDefaultValues` | ✅ | ✅ | ✅ | a field declares a `DefaultValue` |
| | `EnableComputedFields` | ❌ | ✅ | ✅ | a field declares a `ComputedExpression` (EF/static only) |
| | `EnableFieldProjection` | ❌ | ✅ | ✅ | the request sends `?fields=` |
| **Security** | `EnableTenantIsolation` | ❌ | ✅ | ✅ | the resource has `[CrudTenant]` and a tenant can be resolved |
| | `EnableRowLevelSecurity` | ❌ | ✅ | ✅ | an `IResourceFilter<T>` is registered |
| | `EnableResourceAuthorization` | ❌ | ✅ | ✅ | an `IResourceAuthorizer<T>` is registered |
| | `EnableFieldAuthorization` | ❌ | ✅ | ✅ | an `IFieldAuthorizer` is registered |
| **Observability** | `EnableAuditLogging` | ❌ | ✅ | ✅ | an `IAuditLogger` is registered |
| | `EnableMetrics` | ❌ | ✅ | ✅ | `DataSurfaceMetrics` is registered |
| | `EnableTracing` | ❌ | ✅ | ✅ | a listener subscribes to the `DataSurface` activity source |
| **Caching** | `EnableQueryCaching` | ❌ | ✅ | ✅ | an `IQueryResultCache` is registered |
| **Extensibility** | `EnableHooks` | ❌ | ✅ | ✅ | an `ICrudHook` / `ICrudHook<T>` is registered |
| | `EnableOverrides` | ❌ | ✅ | ✅ | an override is registered in `CrudOverrideRegistry` |
| **Integration** | `EnableWebhooks` | ❌ | ❌ | ✅ | an `IWebhookPublisher` is registered |

---

## Always-On Behavior

The following are **always enforced** regardless of feature flags:

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
