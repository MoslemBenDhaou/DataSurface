# Security

DataSurface provides a layered security model: authorization policies, tenant isolation, row-level security, resource-level authorization, field-level access control, and API key authentication. Each layer is independent and composable.

---

## Authorization Policies

Set ASP.NET Core authorization policies per operation using `[CrudAuthorize]`:

```csharp
[CrudAuthorize(Policy = "AdminOnly")]  // All operations
[CrudAuthorize(Operation = CrudOperation.Delete, Policy = "SuperAdmin")]  // Override for delete
public class User { /* ... */ }
```

- Policies are evaluated by ASP.NET Core's `IAuthorizationService`
- Per-operation policies override the class-level policy
- If no policy is set, the endpoint is anonymous (unless `RequireAuthorizationByDefault` is enabled)

### Default Authorization

Require authentication on all endpoints:

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    RequireAuthorizationByDefault = true,
    DefaultPolicy = "Authenticated"
});
```

---

## Tenant Isolation

Automatic multi-tenancy via the `[CrudTenant]` attribute. Tenant isolation ensures users can only access data belonging to their tenant.

```csharp
[CrudResource("orders")]
public class Order
{
    [CrudKey]
    public int Id { get; set; }

    [CrudTenant(ClaimType = "tenant_id", Required = true)]
    public string TenantId { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
    public string ProductName { get; set; } = default!;
}
```

### Behavior

| Operation | Behavior |
|-----------|----------|
| **List / Get** | Automatically filters results to the user's tenant |
| **Create** | Automatically sets the tenant field from the user's claim |
| **Update / Delete** | Validates the resource belongs to the user's tenant |

### Configuration

| Property | Description |
|----------|-------------|
| `ClaimType` | Claim type to extract tenant ID from (e.g., `"tenant_id"`, `"org_id"`) |
| `Required` | If `true`, requests without the tenant claim are rejected with 401 |

### Custom Tenant Resolution

For advanced scenarios (header-based, subdomain-based, database lookup), implement `ITenantResolver`:

```csharp
public class CustomTenantResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _http;

    public CustomTenantResolver(IHttpContextAccessor http) => _http = http;

    public string? ResolveTenantId(TenantContract tenant)
    {
        return _http.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    }
}

builder.Services.AddScoped<ITenantResolver, CustomTenantResolver>();
```

### Feature Flag

```csharp
opt.Features.EnableTenantIsolation = true;  // default: true in Standard/Full
```

---

## Row-Level Security

Filter queries based on user context using `IResourceFilter<T>`:

```csharp
using DataSurface.EFCore.Interfaces;

public class TenantResourceFilter : IResourceFilter<Order>
{
    private readonly ITenantContext _tenant;

    public TenantResourceFilter(ITenantContext tenant) => _tenant = tenant;

    public Expression<Func<Order, bool>>? GetFilter(ResourceContract contract)
        => o => o.TenantId == _tenant.TenantId;
}

builder.Services.AddScoped<IResourceFilter<Order>, TenantResourceFilter>();
```

- Filters apply automatically to List, Get, Update, and Delete operations
- Users can only access records matching the filter
- Non-generic `IResourceFilter` is also available for dynamic type filtering

### Feature Flag

```csharp
opt.Features.EnableRowLevelSecurity = true;  // default: true in Standard/Full
```

---

## Resource Authorization

Authorize access to specific resource instances using `IResourceAuthorizer<T>`:

```csharp
using DataSurface.EFCore.Interfaces;

public class OrderAuthorizer : IResourceAuthorizer<Order>
{
    private readonly IHttpContextAccessor _http;

    public OrderAuthorizer(IHttpContextAccessor http) => _http = http;

    public Task<AuthorizationResult> AuthorizeAsync(
        ResourceContract contract,
        Order? entity,
        CrudOperation operation,
        CancellationToken ct)
    {
        var userId = _http.HttpContext?.User.FindFirst("sub")?.Value;

        if (entity?.OwnerId == userId)
            return Task.FromResult(AuthorizationResult.Success());

        if (_http.HttpContext?.User.IsInRole("Admin") == true)
            return Task.FromResult(AuthorizationResult.Success());

        return Task.FromResult(AuthorizationResult.Fail("Access denied."));
    }
}

builder.Services.AddScoped<IResourceAuthorizer<Order>, OrderAuthorizer>();
```

- **Instance-level checks** — "Can this user access Order #123?"
- **Operation-specific** — Different rules for Get vs Update vs Delete
- **Non-generic option** — `IResourceAuthorizer` for global policies across all resources

### Integration with ASP.NET Core Authorization

```csharp
public class PolicyResourceAuthorizer : IResourceAuthorizer
{
    private readonly IAuthorizationService _auth;
    private readonly IHttpContextAccessor _http;

    public PolicyResourceAuthorizer(IAuthorizationService auth, IHttpContextAccessor http)
    {
        _auth = auth;
        _http = http;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(
        ResourceContract contract, object? entity,
        CrudOperation operation, CancellationToken ct)
    {
        var user = _http.HttpContext?.User;
        if (user is null)
            return AuthorizationResult.Fail("No authenticated user.");

        var policyName = $"{contract.ResourceKey}.{operation}";
        var result = await _auth.AuthorizeAsync(user, entity, policyName);

        return result.Succeeded
            ? AuthorizationResult.Success()
            : AuthorizationResult.Fail("Access denied by policy.");
    }
}

builder.Services.AddScoped<IResourceAuthorizer, PolicyResourceAuthorizer>();
```

### Feature Flag

```csharp
opt.Features.EnableResourceAuthorization = true;  // default: true in Standard/Full
```

---

## Field Authorization

Control which fields individual users can read or write using `IFieldAuthorizer`:

```csharp
using DataSurface.EFCore.Interfaces;

public class SensitiveFieldAuthorizer : IFieldAuthorizer
{
    private readonly IHttpContextAccessor _http;

    public SensitiveFieldAuthorizer(IHttpContextAccessor http) => _http = http;

    public bool CanReadField(ResourceContract contract, string fieldName)
    {
        if (fieldName == "salary")
            return _http.HttpContext?.User.IsInRole("HR") ?? false;
        return true;
    }

    public bool CanWriteField(ResourceContract contract, string fieldName, CrudOperation op)
    {
        if (fieldName == "isAdmin")
            return _http.HttpContext?.User.IsInRole("Admin") ?? false;
        return true;
    }
}

builder.Services.AddScoped<IFieldAuthorizer, SensitiveFieldAuthorizer>();
```

- **Read redaction** — Unauthorized fields are removed from responses
- **Write validation** — Unauthorized field writes throw `UnauthorizedAccessException`

### Feature Flag

```csharp
opt.Features.EnableFieldAuthorization = true;  // default: true in Standard/Full
```

---

## API Key Authentication

Enable API key authentication for machine-to-machine access:

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableApiKeyAuth = true,
    ApiKeyHeaderName = "X-Api-Key"  // default header name
});
```

**Request:**
```http
GET /api/users
X-Api-Key: your-api-key-here
```

### Custom Validation

Implement `IApiKeyValidator` for database-backed or custom validation:

```csharp
using DataSurface.Http;

public class DatabaseApiKeyValidator : IApiKeyValidator
{
    private readonly AppDbContext _db;

    public DatabaseApiKeyValidator(AppDbContext db) => _db = db;

    public async Task<bool> ValidateAsync(string apiKey, CancellationToken ct)
    {
        return await _db.ApiKeys
            .AnyAsync(k => k.Key == apiKey && k.IsActive && k.ExpiresAt > DateTime.UtcNow, ct);
    }
}

builder.Services.AddScoped<IApiKeyValidator, DatabaseApiKeyValidator>();
```

| Scenario | Behavior |
|----------|----------|
| No `IApiKeyValidator` registered | Any non-empty API key is accepted |
| `IApiKeyValidator` registered | Validator determines validity |
| Missing or invalid API key | HTTP 401 Unauthorized |

---

## Rate Limiting

Integrate with ASP.NET Core rate limiting:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("DataSurfacePolicy", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 10;
    });
});

app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableRateLimiting = true,
    RateLimitingPolicy = "DataSurfacePolicy"
});

app.UseRateLimiter();
```

---

## Security Evaluation Order

When multiple security layers are active, they are evaluated in this order:

1. **Authorization policy** — ASP.NET Core policy check
2. **Tenant isolation** — Tenant claim validation and filter
3. **Row-level security** — `IResourceFilter<T>` query filter
4. **Resource authorization** — `IResourceAuthorizer<T>` instance check
5. **Field authorization** — `IFieldAuthorizer` per-field check (on response)

A failure at any layer short-circuits the request.

---

## Related

- [Request Lifecycle](../architecture/request-lifecycle.md) — Full request flow including security stages
- [Feature Flags](feature-flags.md) — Enable/disable individual security features
