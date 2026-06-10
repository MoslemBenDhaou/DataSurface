# Concurrency

DataSurface supports optimistic concurrency control via row version tokens and HTTP ETag headers. This prevents lost updates when multiple clients modify the same resource simultaneously.

---

## How It Works

1. **GET** response includes an `ETag` header derived from the row version
2. **PATCH/PUT** request includes `If-Match` header with the ETag value
3. If the resource has been modified since the ETag was issued, the server returns `409 Conflict`

---

## Setup

Mark a property as the concurrency token:

```csharp
[CrudResource("users")]
public class User
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
    public string Email { get; set; } = default!;

    [CrudConcurrency(RequiredOnUpdate = true)]
    public byte[] RowVersion { get; set; } = default!;
}
```

The `RowVersion` property is automatically configured as an EF Core concurrency token when `EnableRowVersionConvention = true` (the default).

The token is **auto-exposed read-only** in the read shape even when the property has no `[CrudField]` attribute, so clients can always obtain it. Its API name follows a `[CrudField(ApiName = ...)]` override when one is present. `byte[]` tokens are serialized as base64 strings.

---

## Request Flow

### Step 1 — Read the resource

```http
GET /api/users/1
```

```http
HTTP/1.1 200 OK
ETag: W/"AAAAAAB="
Content-Type: application/json

{"id": 1, "email": "alice@example.com", "rowVersion": "AAAAAAB="}
```

### Step 2 — Update with concurrency check

```http
PATCH /api/users/1
If-Match: W/"AAAAAAB="
Content-Type: application/json

{"email": "alice.new@example.com"}
```

### Step 3a — Success (resource unchanged since read)

```http
HTTP/1.1 200 OK
ETag: W/"AAAAAAC="

{"id": 1, "email": "alice.new@example.com"}
```

### Step 3b — Conflict (resource modified by another client)

```http
HTTP/1.1 409 Conflict

{
  "title": "Concurrency conflict",
  "status": 409,
  "detail": "The record was modified by another request. Please refresh and try again."
}
```

### If-Match Notes

- `If-Match: *` follows RFC 9110 — it means "proceed if the resource exists" and performs no token comparison
- `DELETE` also honors `If-Match`: a stale token returns 409
- A token that is not valid base64 returns 400

---

## Configuration Options

### CrudConcurrency Attribute

```csharp
[CrudConcurrency(RequiredOnUpdate = true)]
public byte[] RowVersion { get; set; } = default!;
```

| Property | Default | Description |
|----------|---------|-------------|
| `RequiredOnUpdate` | `true` | Whether the `If-Match` header is required on PATCH/PUT |

### Concurrency Modes

| Mode | Description |
|------|-------------|
| `None` | No concurrency control |
| `RowVersion` | `byte[]` row version token (EF Core concurrency token) |
| `ETag` | HTTP ETag-based token |

### ETag Options

ETags are controlled via `DataSurfaceHttpOptions`:

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableEtags = true  // default: false (opt-in)
});
```

### Dynamic Resources

Dynamic (runtime-defined) resources support row-version concurrency end-to-end: the token is projected into reads as base64, conflicts are enforced by EF Core original-value tracking at `SaveChanges`, and `If-Match` is honored on PATCH and DELETE.

---

## Response Caching with ETags

ETags also enable conditional GET responses:

```http
GET /api/users/1
If-None-Match: W/"AAAAAAB="
```

If the resource hasn't changed:

```http
HTTP/1.1 304 Not Modified
```

`If-None-Match` accepts comma-separated ETag lists, weak (`W/`) prefixes, and `*`, compared per RFC 9110 weak comparison.

This reduces bandwidth for clients that cache responses. See [Caching](caching.md) for more details.

---

## Related

- [CRUD Operations](crud-operations.md) — PATCH vs PUT semantics
- [Caching](caching.md) — Response caching and conditional GET
- [Error Responses](../reference/error-responses.md) — 409 Conflict details
