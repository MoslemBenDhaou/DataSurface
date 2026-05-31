# OpenAPI Integration

DataSurface integrates with Swashbuckle to generate typed OpenAPI/Swagger schemas from ResourceContracts. It also provides a built-in JSON Schema endpoint for each resource.

---

## Swagger/Swashbuckle Setup

```csharp
using DataSurface.OpenApi;

builder.Services.AddSwaggerGen(swagger =>
{
    builder.Services.AddDataSurfaceOpenApi(swagger);
});
```

### What It Generates

- **Typed request schemas** — Per-resource create and update body shapes
- **Typed response schemas** — Per-resource read shapes with correct field types
- **`PagedResult<T>` schema** — Proper list response wrapper
- **Query parameter documentation** — Filter operators, sort fields, pagination params
- **Validation constraints** — `minLength`, `maxLength`, `minimum`, `maximum`, `pattern`, `enum` values from `FieldValidationContract`

### Generated Schema Example

For a `User` resource with email (required, max 255) and status (allowed values):

```json
{
  "UserCreateDto": {
    "type": "object",
    "properties": {
      "email": { "type": "string", "maxLength": 255 },
      "status": { "type": "string", "enum": ["Active", "Inactive", "Pending"] }
    },
    "required": ["email"]
  },
  "UserReadDto": {
    "type": "object",
    "properties": {
      "id": { "type": "integer", "format": "int32" },
      "email": { "type": "string" },
      "status": { "type": "string" },
      "createdAt": { "type": "string", "format": "date-time" }
    }
  }
}
```

---

## Scalar API Reference

Alongside Swagger UI, DataSurface can serve the **Scalar** API reference over the same OpenAPI document via the `DataSurface.Scalar` package. It is additive — Swagger and Scalar can run side by side, and Scalar needs no service registration of its own (only an exposed OpenAPI document).

```csharp
using DataSurface.Scalar;

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => builder.Services.AddDataSurfaceOpenApi(o));
// ...
app.MapDataSurfaceCrud();
app.UseSwagger();            // serves /swagger/{documentName}/swagger.json
app.MapDataSurfaceScalar();  // Scalar UI at /scalar
```

`MapDataSurfaceScalar` accepts optional arguments:

| Parameter | Default | Description |
|-----------|---------|-------------|
| `endpointPrefix` | `"/scalar"` | Route the Scalar UI is served at |
| `title` | `"DataSurface API"` | Reference UI title |
| `openApiRoutePattern` | `"/swagger/{documentName}/swagger.json"` | OpenAPI JSON route (use `/openapi/{documentName}.json` for `Microsoft.AspNetCore.OpenApi`) |
| `configure` | `null` | Callback to customize `ScalarOptions` (theme, layout, servers, auth) |

---

## Schema Endpoint

DataSurface provides a built-in JSON Schema endpoint for every resource:

```http
GET /api/$schema/users
```

### Response

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "urn:datasurface:User",
  "title": "User",
  "type": "object",
  "properties": {
    "id": { "type": "integer", "format": "int32" },
    "email": { "type": "string", "maxLength": 255 },
    "createdAt": { "type": "string", "format": "date-time" }
  },
  "required": ["email"],
  "x-operations": {
    "list": { "enabled": true },
    "get": { "enabled": true },
    "create": { "enabled": true, "requiredOnCreate": ["email"] },
    "update": { "enabled": true },
    "delete": { "enabled": true }
  },
  "x-query": {
    "maxPageSize": 200,
    "filterableFields": ["email", "createdAt"],
    "sortableFields": ["email", "createdAt"]
  }
}
```

### Use Cases

- **Client-side form generation** — Build UI forms from schema definitions
- **API documentation** — Machine-readable contract documentation
- **Contract validation** — Verify client expectations against server contracts
- **Code generation** — Generate typed clients from schema

---

## Resource Discovery

List all available resources:

```http
GET /api/$resources
```

Returns metadata about all registered resources (both static and dynamic), including routes, enabled operations, and field counts.

---

## Related

- [CRUD Operations](crud-operations.md) — Endpoint details
- [Validation](validation.md) — How validation rules map to schema constraints
- [API Endpoints Reference](../reference/api-endpoints.md) — Complete endpoint specification
