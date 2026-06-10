# Error Responses Reference

DataSurface uses the RFC 7807 Problem Details format for all error responses.

---

## Response Format

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Human-readable title",
  "status": 400,
  "detail": "Optional additional detail string",
  "traceId": "00-abc123...",
  "errors": {
    "fieldName": ["Error message 1", "Error message 2"]
  }
}
```

| Field | Description |
|-------|-------------|
| `type` | Standard RFC 9110 reference URI for the status code (set by ASP.NET Core) |
| `title` | Human-readable summary |
| `status` | HTTP status code |
| `detail` | Optional additional detail string |
| `traceId` | Request trace identifier (for correlation with logs/tracing) |
| `errors` | Per-field error messages (validation errors only) |

---

## Error Types

### Validation — `400 Bad Request`

**Title:** `Validation failed`

Returned when request body validation fails.

```json
{
  "title": "Validation failed",
  "detail": "One or more validation errors occurred.",
  "status": 400,
  "traceId": "00-abc123...",
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
- Relation writes referencing unknown or inaccessible target ids
- Unsupported filter operator/type combinations or unparseable filter values

---

### Malformed JSON — `400 Bad Request`

**Title:** `Malformed JSON`

Returned when the request body cannot be parsed as JSON (`System.Text.Json.JsonException`).

```json
{
  "title": "Malformed JSON",
  "detail": "The request body is not valid JSON.",
  "status": 400,
  "traceId": "00-abc123..."
}
```

---

### Invalid Format — `400 Bad Request`

**Title:** `Invalid format`

Returned when a provided value has an invalid format — for example, a non-numeric or out-of-range
value for an integer id, or a malformed GUID.

```json
{
  "title": "Invalid format",
  "detail": "The provided value has an invalid format. 'abc' is not a valid integer id.",
  "status": 400,
  "traceId": "00-abc123..."
}
```

---

### Operation Not Allowed — `400 Bad Request`

**Title:** `Operation not allowed`

Returned when a CRUD operation that is disabled on the resource (`EnableCreate = false`, etc.) is
invoked through a mapped surface — dynamic routes, `/bulk`, or in-process calls
(`CrudOperationDisabledException`). Note: generic `InvalidOperationException` /
`ArgumentException` are **not** mapped to 400 — they indicate server bugs and produce a 500.

```json
{
  "title": "Operation not allowed",
  "detail": "Operation 'Delete' is disabled for resource 'widgets'.",
  "status": 400,
  "traceId": "00-abc123..."
}
```

For static resources, disabled operations are simply not mapped as endpoints, so ASP.NET Core
routing returns `404`/`405` instead.

---

### Not Found — `404 Not Found`

**Title:** `Resource not found`

Returned when the requested resource does not exist.

```json
{
  "title": "Resource not found",
  "detail": "'User' record not found for id '999'.",
  "status": 404,
  "traceId": "00-abc123..."
}
```

**Causes:**
- GET, PATCH, PUT, or DELETE with a non-existent ID
- Resource filtered out by row-level security or tenant isolation
- Unknown resource key (`KeyNotFoundException`) — the detail is the generic
  `"The requested resource was not found."` outside the Development environment

---

### Unauthorized — `401 Unauthorized`

**Title:** `Unauthorized`

Returned when authentication is required but not provided or insufficient
(`UnauthorizedAccessException`). Most 401s are issued by the ASP.NET Core authentication
middleware as a challenge rather than a problem-details body.

```json
{
  "title": "Unauthorized",
  "detail": "You are not authorized to perform this operation.",
  "status": 401,
  "traceId": "00-abc123..."
}
```

**Causes:**
- Missing or invalid authentication token
- Missing API key when `EnableApiKeyAuth = true`
- Invalid API key
- Missing tenant claim when `[CrudTenant(Required = true)]`

---

### Forbidden — `403 Forbidden`

Returned when the authenticated user lacks permission. Issued by the ASP.NET Core authorization
middleware (no DataSurface-specific body).

**Causes:**
- Authorization policy check failed (`[CrudAuthorize]`)
- `IResourceAuthorizer<T>` denied access
- `IFieldAuthorizer` denied write access to a field

---

### Conflict — `409 Conflict`

**Title:** `Concurrency conflict` or `Duplicate record`

Returned when an optimistic concurrency conflict is detected, or a unique constraint is violated.

```json
{
  "title": "Concurrency conflict",
  "detail": "The record was modified by another request. Please refresh and try again.",
  "status": 409,
  "traceId": "00-abc123..."
}
```

**Causes:**
- `If-Match` ETag does not match the current row version
- Another client modified the resource between read and update
- Unique/duplicate database constraint violation (title `Duplicate record`, detail
  `"A record with the same unique value already exists."` outside Development)

---

### Too Many Requests — `429 Too Many Requests`

Returned when rate limiting is active and the client exceeds the allowed rate.

**Causes:**
- Rate limiter policy threshold exceeded

---

### Internal Server Error — `500 Internal Server Error`

**Title:** `An unexpected error occurred`

Returned for any unhandled exception — including generic `InvalidOperationException` and
`ArgumentException`, which are treated as server bugs, never client errors.

```json
{
  "title": "An unexpected error occurred",
  "detail": "An internal server error occurred. Please contact support with trace ID: 00-abc123...",
  "status": 500,
  "traceId": "00-abc123..."
}
```

In the Development environment, the response additionally includes the real exception message,
`exceptionType`, and `stackTrace` extensions. In production, only the safe generic message with
the trace ID is returned.

---

## HTTP Status Code Summary

| Status | Meaning | When |
|--------|---------|------|
| `200` | OK | Successful GET, PATCH, PUT |
| `201` | Created | Successful POST |
| `204` | No Content | Successful DELETE |
| `207` | Multi-Status | Bulk request with partial failures |
| `304` | Not Modified | Conditional GET with matching ETag |
| `400` | Bad Request | Validation failure, malformed JSON, invalid id format, disabled operation |
| `401` | Unauthorized | Authentication required |
| `403` | Forbidden | Authorization denied |
| `404` | Not Found | Resource does not exist |
| `405` | Method Not Allowed | HTTP method not mapped on the route (e.g., operation disabled at map time) |
| `409` | Conflict | Concurrency conflict or duplicate record |
| `429` | Too Many Requests | Rate limit exceeded |
| `499` | Client Closed Request | Request cancelled by the client |
| `500` | Internal Server Error | Unexpected server error |
