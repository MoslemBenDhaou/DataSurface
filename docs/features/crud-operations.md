# CRUD Operations

DataSurface generates fully-featured REST endpoints via ASP.NET Core Minimal APIs. Each resource annotated with `[CrudResource]` gets a complete set of CRUD endpoints automatically.

---

## Generated Endpoints

| Method | Endpoint | Operation | Description |
|--------|----------|-----------|-------------|
| `GET` | `/api/{resource}` | List | Paginated list with filtering, sorting, search |
| `HEAD` | `/api/{resource}` | List | Count only — returns `X-Total-Count` header |
| `GET` | `/api/{resource}/{id}` | Get | Single resource by ID |
| `POST` | `/api/{resource}` | Create | Create a new resource |
| `PATCH` | `/api/{resource}/{id}` | Update | Partial update — only provided fields are changed |
| `PUT` | `/api/{resource}/{id}` | Update | Full replacement — all updatable fields required |
| `DELETE` | `/api/{resource}/{id}` | Delete | Delete a resource |

Additional endpoints (when enabled):

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/{resource}/bulk` | Batch create/update/delete |
| `GET` | `/api/{resource}/stream` | NDJSON streaming |
| `GET` | `/api/{resource}/export` | Export data (JSON/CSV) |
| `POST` | `/api/{resource}/import` | Import data |
| `GET` | `/api/$schema/{resource}` | JSON Schema for resource |
| `GET` | `/api/$resources` | List all available resources |

---

## Controlling Available Operations

Disable specific operations per resource:

```csharp
[CrudResource("audit-logs",
    EnableCreate = false,
    EnableUpdate = false,
    EnableDelete = false)]  // Read-only resource
public class AuditLog { /* ... */ }
```

| Property | Default | Effect when `false` |
|----------|---------|---------------------|
| `EnableList` | `true` | `GET /api/{resource}` returns 405 |
| `EnableGet` | `true` | `GET /api/{resource}/{id}` returns 405 |
| `EnableCreate` | `true` | `POST /api/{resource}` returns 405 |
| `EnableUpdate` | `true` | `PATCH /api/{resource}/{id}` returns 405 |
| `EnableDelete` | `true` | `DELETE /api/{resource}/{id}` returns 405 |

---

## Field-Level Control

Control which fields appear in which DTO shapes using `CrudDto` flags:

```csharp
[CrudResource("products")]
public class Product
{
    [CrudKey]
    public int Id { get; set; }

    // Read + Create + Update — full lifecycle field
    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, RequiredOnCreate = true)]
    public string Name { get; set; } = default!;

    // Read + Create only — set once, never update
    [CrudField(CrudDto.Read | CrudDto.Create)]
    public string SKU { get; set; } = default!;

    // Read-only — server-managed
    [CrudField(CrudDto.Read)]
    public DateTime CreatedAt { get; set; }

    // Not exposed — no [CrudField] attribute
    internal string InternalNotes { get; set; } = default!;
}
```

| Flag | Effect |
|------|--------|
| `CrudDto.Read` | Included in GET responses |
| `CrudDto.Create` | Accepted in POST body |
| `CrudDto.Update` | Accepted in PATCH body |
| `CrudDto.Filter` | Can be used in `filter[field]=value` |
| `CrudDto.Sort` | Can be used in `sort=field` |

Properties without `[CrudField]` are not exposed via the API — this is a safe default.

---

## PATCH vs PUT

**PATCH** (partial update) — Only fields present in the request body are updated:

```http
PATCH /api/products/1
Content-Type: application/json

{"name": "Updated Name"}
```

Only `name` is changed; all other fields remain as-is.

**PUT** (full replacement) — All updatable fields must be provided. Missing fields return 400:

```http
PUT /api/products/1
Content-Type: application/json

{"name": "Updated Name", "sku": "NEW-SKU", "price": 29.99}
```

PUT must be explicitly enabled:

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnablePutForFullUpdate = true
});
```

---

## Default Values

Automatically apply defaults when creating resources. Defaults are applied server-side when a field is not provided in the request body:

```csharp
[CrudResource("orders")]
public class Order
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create, DefaultValue = "pending")]
    public string Status { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Create, DefaultValue = 0)]
    public int Priority { get; set; }
}
```

- Defaults are only applied on **create** (POST)
- If the field is provided in the request, the provided value is used
- Works with strings, numbers, and booleans

---

## Computed Fields

Server-calculated read-only fields evaluated at read time:

```csharp
[CrudResource("employees")]
public class Employee
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
    public string FirstName { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
    public string LastName { get; set; } = default!;

    [CrudField(CrudDto.Read, ComputedExpression = "FirstName + ' ' + LastName")]
    public string FullName { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Create)]
    public decimal Salary { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create)]
    public decimal Bonus { get; set; }

    [CrudField(CrudDto.Read, ComputedExpression = "Salary + Bonus")]
    public decimal TotalCompensation { get; set; }
}
```

- Computed fields are **read-only** — cannot be set via POST or PATCH
- Values are calculated fresh on every read
- Expressions reference CLR property names (not API names)
- Supports string concatenation, numeric operations, and property references

---

## HEAD Requests

`HEAD` requests return count information without a response body:

```http
HEAD /api/users?filter[status]=active
```

```http
HTTP/1.1 200 OK
X-Total-Count: 42
X-Page: 1
X-Page-Size: 200
```

Useful for dashboards and "item count" UI elements without transferring data.

---

## Soft Delete

Entities implementing `ISoftDelete` are marked as deleted instead of being permanently removed:

```csharp
using DataSurface.EFCore.Interfaces;

public class User : ISoftDelete
{
    public int Id { get; set; }
    public string Email { get; set; } = default!;
    public bool IsDeleted { get; set; }  // Set to true on DELETE
}
```

- **On delete:** `IsDeleted = true` instead of row removal
- **On queries:** Soft-deleted records are automatically filtered out
- **Disable:** `EnableSoftDeleteFilter = false` in `DataSurfaceEfCoreOptions`

---

## Timestamps

Entities implementing `ITimestamped` get automatic timestamp population:

```csharp
using DataSurface.EFCore.Interfaces;

public class User : ITimestamped
{
    public int Id { get; set; }
    public string Email { get; set; } = default!;
    public DateTime CreatedAt { get; set; }  // Auto-set on insert
    public DateTime UpdatedAt { get; set; }  // Auto-set on insert and update
}
```

- **On insert:** Both `CreatedAt` and `UpdatedAt` set to `DateTime.UtcNow`
- **On update:** Only `UpdatedAt` is refreshed
- **Disable:** `EnableTimestampConvention = false` in `DataSurfaceEfCoreOptions`

---

## In-Process Usage (No HTTP)

All CRUD operations are available without HTTP via `IDataSurfaceCrudService`:

```csharp
var crudService = serviceProvider.GetRequiredService<IDataSurfaceCrudService>();

// List
var result = await crudService.ListAsync("User", querySpec, ct);

// Get
var user = await crudService.GetAsync("User", entityId, ct);

// Create
var created = await crudService.CreateAsync("User", jsonBody, ct);

// Update
var updated = await crudService.UpdateAsync("User", entityId, jsonBody, ct);

// Delete
await crudService.DeleteAsync("User", entityId, ct);
```

Same validation, security, hooks, and contracts apply — no HTTP involved.

---

## Related

- [Querying](querying.md) — Filtering, sorting, pagination, search
- [Validation](validation.md) — Field validation rules
- [API Endpoints Reference](../reference/api-endpoints.md) — Complete endpoint specification
