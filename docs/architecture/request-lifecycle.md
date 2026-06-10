# Request Lifecycle

This document describes how an HTTP request flows through the DataSurface pipeline from endpoint to response.

---

## Overview

Every DataSurface request passes through these stages:

```
HTTP Request
    │
    ▼
┌─────────────────┐
│  Endpoint Layer  │  DataSurface.Http
│  Route matching  │  Query parsing, API key check, rate limiting
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Contract Resolve │  IResourceContractProvider
│ Load contract    │  Validate operation is enabled
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│   Validation     │  Body validation: required, immutable, unknown fields,
│                  │  length, range, regex, allowed values
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│    Security      │  Authorization policy, tenant isolation,
│                  │  row-level security, resource authorization
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Before Hooks    │  ICrudHook.BeforeAsync, ICrudHook<T>.BeforeXxxAsync
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Override Check  │  CrudOverrideRegistry — if override exists, execute it
│                  │  and skip the default implementation
└────────┬────────┘
         │ (no override)
         ▼
┌─────────────────┐
│  CRUD Operation  │  EfDataSurfaceCrudService or DynamicDataSurfaceCrudService
│  Database I/O    │  Query engine, mapper, EF Core / JSON storage
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  After Hooks     │  ICrudHook.AfterAsync, ICrudHook<T>.AfterXxxAsync
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Post-Operation  │  Audit logging, webhook publishing,
│                  │  cache invalidation, metrics, tracing
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Response Build  │  JSON serialization, field projection,
│                  │  field authorization redaction, ETag, headers
└────────┬────────┘
         │
         ▼
HTTP Response
```

---

## Stage Details

### 1. Endpoint Layer

The `DataSurfaceEndpointMapper` registers Minimal API routes for each resource. When a request arrives:

1. **Route matching** — ASP.NET Core matches the route to a DataSurface handler
2. **Rate limiting** — If enabled, ASP.NET Core rate limiting middleware runs
3. **API key authentication** — If enabled, the `X-Api-Key` header is validated
4. **Query parsing** — `DataSurfaceQueryParser` extracts `page`, `pageSize`, `sort`, `filter[...]`, `expand`, `fields`, and `q` from the query string into a `QuerySpec`

### 2. Contract Resolution

The handler resolves the `ResourceContract` by route (for static) or resource key (for dynamic):

- **Static resources** — looked up from the contract registry built at startup
- **Dynamic resources** — resolved from `DynamicContractProvider` (database-backed)
- **Composite** — `CompositeResourceContractProvider` checks both sources

If the requested operation is disabled in the contract (e.g., `EnableDelete = false`), the service throws `CrudOperationDisabledException`, which the error mapper returns as a `400` problem response. (Generic `InvalidOperationException`/`ArgumentException` are treated as server bugs and map to safe `500`s.)

### 3. Validation (Write Operations)

For `POST`, `PATCH`, and `PUT` requests, the body is validated against the contract:

| Check | Applies To | Error |
|-------|-----------|-------|
| Unknown fields rejected | Create, Update | 400 |
| `RequiredOnCreate` fields present | Create | 400 |
| `Immutable` fields not in PATCH body | Update | 400 |
| `MinLength` / `MaxLength` | Create, Update | 400 |
| `Min` / `Max` | Create, Update | 400 |
| `Regex` pattern match | Create, Update | 400 |
| `AllowedValues` membership | Create, Update | 400 |
| Concurrency token present | Update (if required) | 400 |

### 4. Security Pipeline

Security checks run in this order:

1. **Authorization policy** — ASP.NET Core policy from `[CrudAuthorize]` / `SecurityContract`
2. **Tenant isolation** — `[CrudTenant]` attribute filters and sets tenant context
3. **Row-level security** — `IResourceFilter<T>` applies query-level filters
4. **Resource authorization** — `IResourceAuthorizer<T>` checks instance-level access on every operation: Get/Update/Delete, Create (with the to-be-created instance), and per item on List results
5. **Field authorization** — `IFieldAuthorizer` controls per-field read/write access; redaction applies to read **and** create/update responses (and the webhook payloads built from them)

### 5. Hooks (Before)

If hooks are enabled (`EnableHooks = true`):

1. **Global hooks** (`ICrudHook`) run first, ordered by `Order` property
2. **Typed hooks** (`ICrudHook<T>`) run next, also ordered
3. **Resource hooks** (`ICrudHookResource`) run for dynamic resources

Hooks can modify the entity, body, or context before the operation executes.

### 6. Override Check

The `CrudOverrideRegistry` is checked for a registered override for this resource + operation. If found, the override delegate executes and the default implementation is skipped entirely.

### 7. CRUD Execution

The actual database operation runs:

- **List** — Query engine applies filters, sorting, pagination, and search to `IQueryable`; ordering is always deterministic — the requested sort, else the contract's `DefaultSort`, with the key appended as a final tie-breaker before `Skip`/`Take`
- **Get** — Entity resolved by ID, security filters applied
- **Create** — Body mapped to entity, default values applied, entity added to DbContext
- **Update** — Entity loaded, body fields merged, concurrency checked
- **Delete** — Entity loaded, soft-delete or hard-delete executed

### 8. Hooks (After)

After hooks fire in the same order as before hooks, allowing post-operation logic such as sending notifications or updating caches.

### 9. Post-Operation Processing

These run after the operation completes:

| Feature | Trigger |
|---------|---------|
| **Audit logging** | `IAuditLogger.LogAsync()` with operation details |
| **Webhook publishing** | `IWebhookPublisher.PublishAsync()` for create/update/delete |
| **Cache invalidation** | `IQueryResultCache` entries cleared for the affected resource |
| **Metrics** | Operation counter incremented, duration histogram recorded |
| **Tracing** | Activity/span completed with attributes |

### 10. Response Building

The response is assembled:

- **Entity → JSON** — Entity properties mapped to JSON using contract field definitions
- **Field projection** — If `?fields=` was specified, only requested fields are included
- **Field authorization** — `IFieldAuthorizer.CanReadField()` redacts unauthorized fields
- **Computed fields** — Computed expressions evaluated and injected
- **ETag** — Generated from entity state and included in response headers
- **Pagination headers** — `X-Total-Count`, `X-Page`, `X-Page-Size` set for list responses

---

## Read Operations (List / Get)

```
Request → Parse QuerySpec → Resolve Contract → Security Filters → Query Engine
    → Apply Filters/Sort/Pagination → Execute Query → Map to JSON
    → Field Projection → Field Authorization → Computed Fields → Cache → Respond
```

For cached reads, the cache is checked before query execution. Global hooks run **before** the cache check (a cache hit cannot silently skip them), and cache hits still write audit entries; on a hit, the cached response is then served without executing the query.

## Write Operations (Create / Update / Delete)

```
Request → Parse Body → Resolve Contract → Validate Body → Security Checks
    → Before Hooks → Override Check → Execute Operation → After Hooks
    → Audit Log → Webhook → Cache Invalidation → Map to JSON → Respond
```

---

## Next

- [CRUD Operations](../features/crud-operations.md) — Endpoint details and operation control
- [Security](../features/security.md) — Full security pipeline documentation
