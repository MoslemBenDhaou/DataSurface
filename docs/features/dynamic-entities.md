# Dynamic Entities

DataSurface supports runtime-defined resources — entities created from database metadata without recompilation. Dynamic entities share the same contract system, validation, security, and hook pipeline as static EF Core entities.

---

## Overview

| Aspect | Static Resources | Dynamic Resources |
|--------|-----------------|-------------------|
| **Definition** | C# attributes at compile time | Database metadata at runtime |
| **Storage** | EF Core `DbContext` | JSON records in metadata tables |
| **Contract source** | `ContractBuilder` | `DynamicContractBuilder` |
| **Backend** | `EfCore` | `DynamicJson`, `DynamicEav`, `DynamicHybrid` |
| **Recompilation** | Required for changes | Not required |

---

## Setup

### Step 1: Add Dynamic Tables to Your DbContext

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.AddDataSurfaceDynamic(schema: "dbo");
}
```

### Step 2: Register Dynamic Services

```csharp
using DataSurface.Dynamic.DI;

// Static contracts (if any)
builder.Services.AddDataSurfaceEfCore(opt => { /* ... */ });

// Dynamic contracts
builder.Services.AddDataSurfaceDynamic(opt =>
{
    opt.Schema = "dbo";
    opt.WarmUpContractsOnStart = true;  // default: true
});
```

`AddDataSurfaceDynamic` also registers the `CompositeResourceContractProvider` as the app-wide `IResourceContractProvider` and the `DataSurfaceCrudRouter` as the `IDataSurfaceCrudService`, so both static and dynamic resources resolve and route correctly — no manual registration is needed. When `WarmUpContractsOnStart` is `true`, a hosted service loads all dynamic contracts into a shared cache at startup.

### Step 3: Map Endpoints with Dynamic Catch-All

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    MapStaticResources = true,
    MapDynamicCatchAll = true   // Enables /api/d/{route}
});
```

Dynamic resources are served under a separate prefix (default: `/d`) to avoid route collisions with static resources:

```
GET /api/d/{dynamicRoute}
```

---

## Storage Backends

| Backend | Description |
|---------|-------------|
| `DynamicJson` | Each record stored as a single JSON document. Simple, flexible. |
| `DynamicEav` | Entity-Attribute-Value storage. Good for sparse data. |
| `DynamicHybrid` | Combines structured columns with JSON overflow. Balance of performance and flexibility. |

Regardless of backend, dynamic records share these semantics:

- **Canonical ids** — `Guid` ids are canonicalized to the standard `"D"` format everywhere (storage, routes, index rows, responses); typed keys round-trip with their JSON type (e.g., `int` keys come back as JSON numbers).
- **Id collisions** — a client-supplied id that matches an existing record returns `400`.
- **Atomic writes** — a record and its index rows are written in a single `SaveChanges` (the index service stages rows on the same change tracker instead of saving separately).
- **Deletes** — soft delete removes the record's index rows; hard delete can also purge a previously soft-deleted id.
- **Optimistic concurrency** — works end-to-end: the rowversion is projected into reads as a base64 token, enforced via EF original values on update, and `If-Match` is honored on both `PATCH` and `DELETE` (invalid tokens return `400`).
- **DateTime normalization** — `DateTime` values are normalized to UTC.

---

## Entity Definitions

Dynamic resources are defined by `EntityDef` and `PropertyDef` records in the database:

### EntityDef

Represents a dynamic resource definition:
- Resource key and route
- Storage backend type
- Max page size and expand depth
- Enabled operations (list, get, create, update, delete)

### PropertyDef

Represents a field within a dynamic entity:
- Field name and API name
- Field type (string, int, decimal, boolean, datetime, etc.)
- DTO inclusion flags (read, create, update, filter, sort)
- Validation rules (required, min/max length, min/max value, regex, allowed values)
- Computed expressions and default values
- Searchable flag

`DynamicContractBuilder` validates definitions when building contracts — invalid runtime metadata is rejected instead of silently producing a broken contract. Client-supplied field names are stored under the canonical contract casing, so casing differences in request bodies do not create duplicate JSON keys.

> **Runtime parity note:** validation, filtering, sorting, and field projection are enforced for dynamic resources today. **Default values, computed fields, and full-text search (`?q=`) currently apply to EF/static resources only** — dynamic support for these is on the [roadmap](../roadmap.md).

---

## Admin API

Manage dynamic entity definitions via REST using `DataSurface.Admin`:

```csharp
using DataSurface.Admin.DI;
using DataSurface.Admin;

builder.Services.AddDataSurfaceAdmin();

app.MapDataSurfaceAdmin(new DataSurfaceAdminOptions
{
    Prefix = "/admin/ds",
    RequireAuthorization = true,
    Policy = "DataSurfaceAdmin"
});
```

### Admin Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/admin/ds/entities` | List all entity definitions |
| `GET` | `/admin/ds/entities/{key}` | Get single entity definition |
| `PUT` | `/admin/ds/entities/{key}` | Create or update entity definition |
| `DELETE` | `/admin/ds/entities/{key}` | Delete entity definition |
| `GET` | `/admin/ds/export` | Export all definitions as JSON |
| `POST` | `/admin/ds/import` | Import definitions from JSON |
| `POST` | `/admin/ds/entities/{key}/reindex` | Rebuild search indexes |

### Admin DTOs

The admin API accepts and returns `AdminEntityDefDto` and `AdminPropertyDefDto` objects, which map directly to the underlying `EntityDef` and `PropertyDef` metadata.

### Admin Behavior

- **Validation** — upserts and imports are validated by `DynamicMetadataValidator`, which rejects invalid `MaxPageSize` (< 1) and `MaxExpandDepth` (outside 0–3), non-dynamic backends (only `DynamicJson`/`DynamicEav`/`DynamicHybrid` are allowed), invalid entity keys/routes, ambiguous key-field injection, and entity keys or routes that collide with statically defined resources.
- **Delete purges data** — `DELETE /admin/ds/entities/{key}` removes the definition **and** all of its stored records and index rows, so re-creating the same key later cannot resurrect old data.
- **Automatic reindex** — when an upsert or import changes an entity's property definitions, its filter/sort indexes are rebuilt automatically (no manual reindex call needed).
- **Atomic import** — imports validate every entity first and apply them in a single transaction where the provider supports one; any failure rolls the whole import back.

---

## Dynamic Resource Hooks

Dynamic resources use `ICrudHookResource` instead of typed hooks:

```csharp
using DataSurface.Dynamic.Hooks;

public class DynamicResourceHook : ICrudHookResource
{
    public Task BeforeCreateAsync(string resourceKey, JsonObject body, CrudHookContext ctx)
    {
        // Custom logic before creating a dynamic resource record
        return Task.CompletedTask;
    }

    public Task AfterCreateAsync(string resourceKey, JsonObject created, CrudHookContext ctx)
    {
        return Task.CompletedTask;
    }
}
```

The `CrudResourceHookDispatcher` manages hook resolution and execution for dynamic resources, dispatching to all registered `ICrudHookResource` implementations.

---

## Indexing

Dynamic resources support filter/sort indexing via the `IDynamicIndexService`. Indexes are automatically maintained on create/update/delete (staged on the same change tracker as the record and saved atomically in a single `SaveChanges`), rebuilt automatically when an entity's property definitions change, and can be manually rebuilt via the admin reindex endpoint.

Index values are stored as raw values rather than JSON-escaped text, so filters on non-ASCII text match correctly, and the numeric index column has explicit `decimal(38,12)` precision to avoid silent truncation.

---

## Coexistence with Static Resources

When both static and dynamic resources are registered:

1. `CompositeResourceContractProvider` merges contracts from both sources
2. `DataSurfaceCrudRouter` routes operations to the correct backend service
3. Resource discovery (`GET /api/$resources`) lists both static and dynamic resources
4. Schema endpoint (`GET /api/$schema/{route}`) works for both

Dynamic contracts are held in a shared singleton `DynamicContractCache`, stamped with each definition row's `UpdatedAt`. This is what makes dynamic resources visible to discovery, schema, and health endpoints across scopes — and stale cached contracts are rebuilt automatically when their definition changes.

---

## Security & Authorization

Dynamic resources run through the same security pipeline as static resources:

- **Per-operation policies** — when a dynamic resource's definition declares authorization policies, the dynamic catch-all enforces them via `IAuthorizationService`, returning `403 Forbidden` or a `401` challenge as appropriate.
- **Resource & field authorization** — `IResourceAuthorizer` (instance-level access) and `IFieldAuthorizer` (field read/write) apply to dynamic records, including per-item resource authorization on list results (denied items are omitted) and redaction of unauthorized fields from responses.
- **Tenant isolation** — tenant filtering and auto-stamping apply: dynamic records are filtered to the caller's tenant and stamped with it on create.
- **Audit logging** — `IAuditLogger` records dynamic CRUD operations.

> The dynamic catch-all is opt-in: routes are only mapped when `MapDynamicCatchAll = true` (default `false`). When `RequireAuthorizationByDefault` is enabled, unauthenticated requests are challenged **before** the route lookup, so anonymous callers cannot probe which dynamic routes exist.

---

## Configuration

```csharp
builder.Services.AddDataSurfaceDynamic(opt =>
{
    opt.Schema = "dbo";                     // DB schema for dynamic tables
    opt.WarmUpContractsOnStart = true;      // Load contracts at startup via a hosted service (default: true)
});
```

```csharp
app.MapDataSurfaceAdmin(new DataSurfaceAdminOptions
{
    Prefix = "/admin/ds",                   // Route prefix
    RequireAuthorization = true,            // Require auth
    Policy = "DataSurfaceAdmin"             // Auth policy name
});
```

---

## Related

- [Architecture Overview](../architecture/overview.md) — How static and dynamic backends coexist
- [Hooks & Overrides](hooks-and-overrides.md) — Hook types for dynamic resources
- [Configuration Options](../reference/configuration-options.md) — `DataSurfaceDynamicOptions` and `DataSurfaceAdminOptions`
