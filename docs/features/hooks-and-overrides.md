# Hooks & Overrides

DataSurface provides two extensibility mechanisms: **hooks** for injecting logic around CRUD operations, and **overrides** for completely replacing CRUD operations with custom implementations.

---

## Hooks

Hooks run before and after CRUD operations without replacing the default behavior. They are ideal for cross-cutting concerns like logging, notifications, data enrichment, and side effects.

### Global Hooks

Run for **all resources**. Implement `ICrudHook`:

```csharp
using DataSurface.EFCore.Interfaces;

public class AuditHook : ICrudHook
{
    public int Order => 0;  // Lower values run first

    public Task BeforeAsync(CrudHookContext ctx)
    {
        Console.WriteLine($"Before {ctx.Operation} on {ctx.Contract.ResourceKey}");
        return Task.CompletedTask;
    }

    public Task AfterAsync(CrudHookContext ctx)
    {
        Console.WriteLine($"After {ctx.Operation}");
        return Task.CompletedTask;
    }
}

builder.Services.AddScoped<ICrudHook, AuditHook>();
```

### Entity-Specific Hooks

Run only for a **specific entity type**. Implement `ICrudHook<T>`:

```csharp
public class UserHook : ICrudHook<User>
{
    public int Order => 0;

    public Task BeforeCreateAsync(User entity, JsonObject body, CrudHookContext ctx)
    {
        entity.CreatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task AfterCreateAsync(User entity, CrudHookContext ctx)
    {
        // Send welcome email, publish event, etc.
        return Task.CompletedTask;
    }

    // BeforeUpdateAsync, AfterUpdateAsync, BeforeDeleteAsync, AfterDeleteAsync,
    // BeforeReadAsync, AfterReadAsync — all optional with default no-op implementations
}

builder.Services.AddScoped<ICrudHook<User>, UserHook>();
```

### Resource-Key Hooks (Dynamic Resources)

For dynamic resources (no CLR type), implement `ICrudHookResource`:

```csharp
using DataSurface.Dynamic.Hooks;

public class DynamicAuditHook : ICrudHookResource
{
    public Task BeforeCreateAsync(string resourceKey, JsonObject body, CancellationToken ct)
    {
        // Logic for dynamic resource creation
        return Task.CompletedTask;
    }

    public Task AfterCreateAsync(string resourceKey, JsonObject entity, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    // Before/After for Read, Update, Delete also available
}
```

### Execution Order

1. **Global hooks** (`ICrudHook`) — ordered by `Order` property, ascending
2. **Typed hooks** (`ICrudHook<T>`) — ordered by `Order` property, ascending
3. **Resource hooks** (`ICrudHookResource`) — for dynamic resources

Multiple hooks of the same type are executed in deterministic order based on the `Order` property. Lower values run first.

### Hook Context

The `CrudHookContext` provides access to:

- `Contract` — The ResourceContract for the current resource
- `Operation` — The CRUD operation being performed
- `CancellationToken` — Cancellation support

### Feature Flag

```csharp
opt.Features.EnableHooks = true;  // default: true in Standard/Full
```

---

## Overrides

Overrides completely **replace** the default CRUD implementation for a specific resource and operation. Use when you need entirely custom logic that doesn't fit the contract-driven model.

### Registering an Override

```csharp
var registry = app.Services.GetRequiredService<CrudOverrideRegistry>();

registry.Override("User", CrudOperation.Create,
    async (CreateOverride)((contract, body, ctx, ct) =>
    {
        // Custom creation logic — the default CreateAsync is NOT called
        var user = new User { Email = body["email"]!.GetValue<string>() };
        ctx.Db.Add(user);
        await ctx.Db.SaveChangesAsync(ct);

        return new JsonObject { ["id"] = user.Id, ["email"] = user.Email };
    }));
```

### Override vs Hook

| Aspect | Hook | Override |
|--------|------|----------|
| **Effect** | Runs alongside default logic | Replaces default logic |
| **Scope** | Global or per-entity-type | Per-resource + per-operation |
| **Use case** | Cross-cutting concerns, side effects | Custom business logic |
| **Before/After** | Yes — runs before and after | No — it is the operation |
| **Multiple** | Multiple hooks can coexist | One override per resource+operation |

### When to Use Overrides

- The resource needs a custom data source (not the standard DbContext)
- The create/update logic involves complex multi-step workflows
- The response format must differ from the standard contract-driven output
- You need to call external services as part of the operation

### Feature Flag

```csharp
opt.Features.EnableOverrides = true;  // default: true in Standard/Full
```

---

## Combining Hooks and Overrides

When an override is registered for a resource+operation:

1. **Before hooks** still run before the override
2. **The override** runs instead of the default implementation
3. **After hooks** still run after the override

This allows cross-cutting hooks (audit, logging) to remain active even when the core logic is overridden.

---

## Related

- [Request Lifecycle](../architecture/request-lifecycle.md) — Where hooks and overrides fit in the pipeline
- [Feature Flags](feature-flags.md) — Enable/disable hooks and overrides
