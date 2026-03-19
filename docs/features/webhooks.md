# Webhooks

DataSurface can publish events when CRUD operations occur. Webhooks are useful for integrations, audit trails, event-driven architectures, and triggering downstream workflows.

---

## Setup

### Enable Webhooks

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableWebhooks = true
});
```

### Implement a Publisher

```csharp
using DataSurface.Core.Webhooks;

public class MyWebhookPublisher : IWebhookPublisher
{
    private readonly HttpClient _http;
    private readonly ILogger<MyWebhookPublisher> _logger;

    public MyWebhookPublisher(HttpClient http, ILogger<MyWebhookPublisher> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task PublishAsync(WebhookEvent evt, CancellationToken ct)
    {
        _logger.LogInformation("Webhook: {Operation} on {Resource} id={Id}",
            evt.Operation, evt.ResourceKey, evt.EntityId);

        await _http.PostAsJsonAsync("https://hooks.example.com/datasurface", evt, ct);
    }
}

builder.Services.AddSingleton<IWebhookPublisher, MyWebhookPublisher>();
```

---

## WebhookEvent

Published after every create, update, and delete operation:

| Property | Type | Description |
|----------|------|-------------|
| `ResourceKey` | `string` | The resource that changed (e.g., `"User"`) |
| `Operation` | `CrudOperation` | `Create`, `Update`, or `Delete` |
| `EntityId` | `string` | ID of the affected entity |
| `Timestamp` | `DateTimeOffset` | UTC timestamp of the event |
| `Payload` | `JsonObject?` | JSON representation of the entity (for create/update) |

### Example Payload

```json
{
  "resourceKey": "User",
  "operation": "Create",
  "entityId": "42",
  "timestamp": "2024-12-28T14:30:00Z",
  "payload": {
    "id": 42,
    "email": "alice@example.com",
    "name": "Alice"
  }
}
```

---

## WebhookSubscription

The `WebhookSubscription` record defines how subscriptions are configured:

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `string` | Unique subscription identifier |
| `Url` | `string` | Target URL to receive webhook events |
| `ResourceKey` | `string?` | Filter to specific resource (null = all resources) |
| `Operations` | `IReadOnlyList<CrudOperation>?` | Filter to specific operations (null = all operations) |
| `Secret` | `string?` | Shared secret for HMAC signature verification |
| `IsActive` | `bool` | Whether the subscription is active |

---

## Failure Handling

- Webhook publishing is **fire-and-forget** by default
- Failures are logged but **do not fail** the CRUD operation
- The CRUD operation completes successfully regardless of webhook delivery status
- Implement retry logic, dead-letter queues, or circuit breakers in your `IWebhookPublisher` as needed

---

## Feature Flag

Webhooks are opt-in:

```csharp
// Via HTTP options
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableWebhooks = true
});

// Via feature flags (for the Full preset, webhooks are enabled)
opt.Features = DataSurfaceFeatures.Full;

// Or individually
opt.Features.EnableWebhooks = true;
```

---

## Related

- [Hooks & Overrides](hooks-and-overrides.md) — Lifecycle hooks that run inline with operations
- [Observability](observability.md) — Audit logging for operation tracking
