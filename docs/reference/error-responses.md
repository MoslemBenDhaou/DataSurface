# Error Responses Reference

DataSurface uses the RFC 7807 Problem Details format for all error responses.

---

## Response Format

```json
{
  "type": "https://datasurface/errors/{error-type}",
  "title": "Human-readable title",
  "status": 400,
  "traceId": "00-abc123...",
  "errors": {
    "fieldName": ["Error message 1", "Error message 2"]
  }
}
```

| Field | Description |
|-------|-------------|
| `type` | URI identifying the error type |
| `title` | Human-readable summary |
| `status` | HTTP status code |
| `traceId` | Request trace identifier (for correlation with logs/tracing) |
| `errors` | Per-field error messages (for validation errors) |
| `detail` | Optional additional detail string |

---

## Error Types

### Validation — `400 Bad Request`

**Type:** `https://datasurface/errors/validation`

Returned when request body validation fails.

```json
{
  "type": "https://datasurface/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "errors": {
    "email": ["Required"],
    "password": ["Min length is 8"],
    "age": ["Must be between 0 and 150"],
    "status": ["Must be one of: Active, Inactive, Pending"],
    "unknownField": ["Unknown field"]
  }
}
```

**Causes:**
- Missing `RequiredOnCreate` fields on POST
- `Immutable` fields present on PATCH
- `MinLength` / `MaxLength` violation
- `Min` / `Max` violation
- `Regex` pattern mismatch
- `AllowedValues` violation
- Unknown fields in request body
- Missing concurrency token when required
- PUT missing required updatable fields

---

### Not Found — `404 Not Found`

**Type:** `https://datasurface/errors/not-found`

Returned when the requested resource does not exist.

```json
{
  "type": "https://datasurface/errors/not-found",
  "title": "Resource not found",
  "status": 404,
  "detail": "User with id '999' was not found."
}
```

**Causes:**
- GET, PATCH, PUT, or DELETE with a non-existent ID
- Resource filtered out by row-level security or tenant isolation

---

### Unauthorized — `401 Unauthorized`

**Type:** `https://datasurface/errors/unauthorized`

Returned when authentication is required but not provided.

```json
{
  "type": "https://datasurface/errors/unauthorized",
  "title": "Authentication required",
  "status": 401
}
```

**Causes:**
- Missing or invalid authentication token
- Missing API key when `EnableApiKeyAuth = true`
- Invalid API key
- Missing tenant claim when `[CrudTenant(Required = true)]`

---

### Forbidden — `403 Forbidden`

**Type:** `https://datasurface/errors/forbidden`

Returned when the authenticated user lacks permission.

```json
{
  "type": "https://datasurface/errors/forbidden",
  "title": "Access denied",
  "status": 403,
  "detail": "You can only access your own orders."
}
```

**Causes:**
- Authorization policy check failed (`[CrudAuthorize]`)
- `IResourceAuthorizer<T>` denied access
- `IFieldAuthorizer` denied write access to a field

---

### Conflict — `409 Conflict`

**Type:** `https://datasurface/errors/conflict`

Returned when an optimistic concurrency conflict is detected.

```json
{
  "type": "https://datasurface/errors/conflict",
  "title": "Concurrency conflict",
  "status": 409,
  "detail": "The resource has been modified by another request."
}
```

**Causes:**
- `If-Match` ETag does not match current row version
- Another client modified the resource between read and update

---

### Method Not Allowed — `405 Method Not Allowed`

Returned when a CRUD operation is disabled for the resource.

**Causes:**
- `EnableCreate = false` and client sends POST
- `EnableUpdate = false` and client sends PATCH/PUT
- `EnableDelete = false` and client sends DELETE

---

### Too Many Requests — `429 Too Many Requests`

Returned when rate limiting is active and the client exceeds the allowed rate.

**Causes:**
- Rate limiter policy threshold exceeded

---

### Internal Server Error — `500 Internal Server Error`

**Type:** `https://datasurface/errors/invalid-metadata`

Returned at startup if contract configuration is invalid.

```json
{
  "type": "https://datasurface/errors/invalid-metadata",
  "title": "Contract configuration error",
  "status": 500
}
```

**Causes:**
- Invalid contract definitions detected during startup validation
- This should not occur in production — fix during development

---

## HTTP Status Code Summary

| Status | Meaning | When |
|--------|---------|------|
| `200` | OK | Successful GET, PATCH, PUT |
| `201` | Created | Successful POST |
| `204` | No Content | Successful DELETE |
| `304` | Not Modified | Conditional GET with matching ETag |
| `400` | Bad Request | Validation failure |
| `401` | Unauthorized | Authentication required |
| `403` | Forbidden | Authorization denied |
| `404` | Not Found | Resource does not exist |
| `405` | Method Not Allowed | Operation disabled |
| `409` | Conflict | Concurrency conflict |
| `429` | Too Many Requests | Rate limit exceeded |
| `500` | Internal Server Error | Server error |
