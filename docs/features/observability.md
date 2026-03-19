# Observability

DataSurface integrates with standard .NET observability infrastructure: structured logging via `ILogger`, OpenTelemetry metrics, distributed tracing, health checks, and audit logging.

---

## Structured Logging

Both `EfDataSurfaceCrudService` and `DynamicDataSurfaceCrudService` emit structured logs for every operation:

```
[DBG] List User page=1 pageSize=20
[DBG] List User completed in 45ms, returned 20/142 items
[INF] Created User in 12ms
[INF] Updated User id=5 in 8ms
[INF] Deleted User id=5 in 3ms
```

### Log Levels

| Level | Operations |
|-------|-----------|
| `Debug` | Operation start, read completions (List, Get) |
| `Information` | Mutating operations (Create, Update, Delete) |

### Structured Properties

All log entries include structured properties for filtering and querying:

| Property | Description |
|----------|-------------|
| `{Resource}` | Resource key (e.g., `"User"`) |
| `{Id}` | Entity ID (when applicable) |
| `{ElapsedMs}` | Operation duration in milliseconds |
| `{Count}` / `{Total}` | List result counts |

These properties work with any structured logging sink (Serilog, Seq, Application Insights, etc.).

---

## Metrics

OpenTelemetry-compatible metrics via `DataSurfaceMetrics`:

### Setup

```csharp
builder.Services.AddSingleton<DataSurfaceMetrics>();

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("DataSurface"));
```

### Available Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `datasurface.operations` | Counter | Total CRUD operations by resource and operation |
| `datasurface.errors` | Counter | Failed operations by resource, operation, and error type |
| `datasurface.operation.duration` | Histogram | Operation duration in milliseconds |
| `datasurface.rows_affected` | Counter | Rows affected by operations |

### Tags/Labels

Each metric includes tags for filtering:
- `resource` — Resource key
- `operation` — CRUD operation name
- `error_type` — Error classification (for error counter)

### Feature Flag

```csharp
opt.Features.EnableMetrics = true;  // default: true in Standard/Full
```

---

## Distributed Tracing

Activity/span integration via `DataSurfaceTracing`:

### Setup

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("DataSurface"));
```

### Trace Attributes

Each CRUD operation creates a span with these attributes:

| Attribute | Description |
|-----------|-------------|
| `datasurface.resource` | Resource key |
| `datasurface.operation` | CRUD operation |
| `datasurface.entity_id` | Entity ID (when applicable) |
| `datasurface.rows_affected` | Rows returned or affected |
| `datasurface.query.page` | Page number (List) |
| `datasurface.query.page_size` | Page size (List) |
| `datasurface.query.filter_count` | Number of active filters (List) |
| `datasurface.query.sort_count` | Number of sort fields (List) |

Traces integrate with any OpenTelemetry-compatible backend (Jaeger, Zipkin, Azure Monitor, etc.).

### Feature Flag

```csharp
opt.Features.EnableTracing = true;  // default: true in Standard/Full
```

---

## Health Checks

Built-in `IHealthCheck` implementations for monitoring:

### Setup

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<DataSurfaceDbHealthCheck>("datasurface-db")
    .AddCheck<DataSurfaceContractsHealthCheck>("datasurface-contracts")
    .AddCheck<DynamicMetadataHealthCheck>("datasurface-dynamic-metadata")
    .AddCheck<DynamicContractsHealthCheck>("datasurface-dynamic-contracts");
```

### Available Health Checks

| Check | Description |
|-------|-------------|
| `DataSurfaceDbHealthCheck` | Database connectivity — verifies the DbContext can connect |
| `DataSurfaceContractsHealthCheck` | Static contracts loaded — verifies at least one contract is registered |
| `DynamicMetadataHealthCheck` | Dynamic entity definitions table accessible |
| `DynamicContractsHealthCheck` | Dynamic contracts loaded from metadata |

Use the standard ASP.NET Core health check endpoint:

```csharp
app.MapHealthChecks("/health");
```

---

## Audit Logging

Track all CRUD operations with full before/after state using `IAuditLogger`:

### Setup

```csharp
using DataSurface.EFCore.Interfaces;

public class DatabaseAuditLogger : IAuditLogger
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;

    public DatabaseAuditLogger(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task LogAsync(AuditLogEntry entry, CancellationToken ct)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _http.HttpContext?.User.FindFirst("sub")?.Value,
            Operation = entry.Operation.ToString(),
            ResourceKey = entry.ResourceKey,
            EntityId = entry.EntityId,
            Timestamp = entry.Timestamp,
            Success = entry.Success,
            Changes = entry.Changes?.ToJsonString(),
            PreviousValues = entry.PreviousValues?.ToJsonString()
        });
        await _db.SaveChangesAsync(ct);
    }
}

builder.Services.AddScoped<IAuditLogger, DatabaseAuditLogger>();
```

### AuditLogEntry Properties

| Property | Type | Description |
|----------|------|-------------|
| `Operation` | `CrudOperation` | The operation performed |
| `ResourceKey` | `string` | Resource that was accessed |
| `EntityId` | `string?` | Entity ID (if applicable) |
| `Timestamp` | `DateTimeOffset` | UTC timestamp |
| `Success` | `bool` | Whether the operation succeeded |
| `Changes` | `JsonObject?` | Fields written (create/update) |
| `PreviousValues` | `JsonObject?` | Previous field values (update) |

### Feature Flag

```csharp
opt.Features.EnableAuditLogging = true;  // default: true in Standard/Full
```

---

## Observability Stack Example

A complete observability setup combining all features:

```csharp
// Metrics + Tracing
builder.Services.AddSingleton<DataSurfaceMetrics>();
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("DataSurface"))
    .WithTracing(tracing => tracing.AddSource("DataSurface"));

// Audit logging
builder.Services.AddScoped<IAuditLogger, DatabaseAuditLogger>();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<DataSurfaceDbHealthCheck>("datasurface-db")
    .AddCheck<DataSurfaceContractsHealthCheck>("datasurface-contracts");

// Feature flags — all observability enabled
opt.Features = new DataSurfaceFeatures
{
    EnableAuditLogging = true,
    EnableMetrics = true,
    EnableTracing = true
};
```

---

## Related

- [Feature Flags](feature-flags.md) — Enable/disable individual observability features
- [Request Lifecycle](../architecture/request-lifecycle.md) — Where observability hooks into the pipeline
