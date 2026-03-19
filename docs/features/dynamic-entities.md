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
using DataSurface.Dynamic.Contracts;

// Static contracts (if any)
builder.Services.AddDataSurfaceEfCore(opt => { /* ... */ });

// Dynamic contracts
builder.Services.AddDataSurfaceDynamic(opt =>
{
    opt.Schema = "dbo";
    opt.WarmUpContractsOnStart = true;
});

// Use composite provider for both static + dynamic
builder.Services.AddScoped<IResourceContractProvider>(sp =>
    sp.GetRequiredService<CompositeResourceContractProvider>());

// Use router to dispatch to correct backend
builder.Services.AddScoped<DataSurfaceCrudRouter>();
builder.Services.AddScoped<IDataSurfaceCrudService>(sp =>
    sp.GetRequiredService<DataSurfaceCrudRouter>());
```

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

---

## Dynamic Resource Hooks

Dynamic resources use `ICrudHookResource` instead of typed hooks:

```csharp
using DataSurface.Dynamic.Hooks;

public class DynamicResourceHook : ICrudHookResource
{
    public Task BeforeCreateAsync(string resourceKey, JsonObject body, CancellationToken ct)
    {
        // Custom logic before creating a dynamic resource record
        return Task.CompletedTask;
    }

    public Task AfterCreateAsync(string resourceKey, JsonObject entity, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
```

The `CrudResourceHookDispatcher` manages hook resolution and execution for dynamic resources, dispatching to all registered `ICrudHookResource` implementations.

---

## Indexing

Dynamic resources support search indexing via the `IDynamicEntityIndexService`. Indexes are automatically maintained on create/update/delete and can be manually rebuilt via the admin reindex endpoint.

---

## Coexistence with Static Resources

When both static and dynamic resources are registered:

1. `CompositeResourceContractProvider` merges contracts from both sources
2. `DataSurfaceCrudRouter` routes operations to the correct backend service
3. Resource discovery (`GET /api/$resources`) lists both static and dynamic resources
4. Schema endpoint (`GET /api/$schema/{route}`) works for both

---

## Configuration

```csharp
builder.Services.AddDataSurfaceDynamic(opt =>
{
    opt.Schema = "dbo";                     // DB schema for dynamic tables
    opt.WarmUpContractsOnStart = true;      // Load contracts at startup
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
