# Architecture Overview

DataSurface is organized as a set of modular NuGet packages, each with a clear responsibility. At the center is the **ResourceContract** — a normalized metadata object that describes everything about a CRUD resource.

---

## Module Structure

```
┌─────────────────────────────────────────────────────────────────┐
│                        HTTP Layer                               │
│  DataSurface.Http                                               │
│  Minimal API mapping, query parsing, ETags, error mapping       │
├─────────────────────────────────────────────────────────────────┤
│                    IDataSurfaceCrudService                       │
│  ListAsync · GetAsync · CreateAsync · UpdateAsync · DeleteAsync  │
├──────────────────────────┬──────────────────────────────────────┤
│  EfDataSurfaceCrudService│  DynamicDataSurfaceCrudService       │
│  DataSurface.EFCore      │  DataSurface.Dynamic                 │
│  Static EF Core entities │  JSON / EAV dynamic records          │
├──────────────────────────┴──────────────────────────────────────┤
│                    ResourceContract                              │
│  DataSurface.Core                                               │
│  Fields · Relations · Operations · Query · Security             │
├──────────────────────────┬──────────────────────────────────────┤
│  ContractBuilder         │  DynamicContractBuilder              │
│  C# attributes → Contract│  DB metadata → Contract              │
└──────────────────────────┴──────────────────────────────────────┘
```

### Supporting Modules

| Module | Role |
|--------|------|
| `DataSurface.Admin` | REST API for managing dynamic entity definitions at runtime |
| `DataSurface.OpenApi` | Swashbuckle operation filters and typed schema generation |
| `DataSurface.Generator` | Roslyn source generator for typed DTOs |

---

## Key Abstractions

| Interface | Package | Purpose |
|-----------|---------|---------|
| `IDataSurfaceCrudService` | EFCore | Executes CRUD operations against a backend |
| `IResourceContractProvider` | EFCore | Resolves `ResourceContract` by resource key or route |
| `ICrudHook` / `ICrudHook<T>` | EFCore | Global and entity-specific lifecycle hooks |
| `CrudOverrideRegistry` | EFCore | Replaces any CRUD operation with custom logic |
| `IResourceFilter<T>` | EFCore | Row-level security — filters queryables per user context |
| `IResourceAuthorizer<T>` | EFCore | Instance-level authorization — "can this user access entity X?" |
| `IFieldAuthorizer` | EFCore | Field-level read/write access control |
| `ITenantResolver` | EFCore | Resolves the current tenant ID from request context |
| `IAuditLogger` | EFCore | Logs all CRUD operations for audit trails |
| `IQueryResultCache` | EFCore | Caches query results via `IDistributedCache` |
| `IWebhookPublisher` | Core | Publishes events when CRUD operations occur |
| `ISoftDelete` | EFCore | Convention interface for soft-delete entities |
| `ITimestamped` | EFCore | Convention interface for auto-timestamp entities |
| `IApiKeyValidator` | Http | Custom API key validation logic |

---

## Contract as Single Source of Truth

Every feature in DataSurface reads from the `ResourceContract`. There is no secondary configuration — the contract is the complete description of a resource.

```
[CrudResource] + [CrudField] + [CrudRelation] + ...
                    │
                    ▼
              ContractBuilder
                    │
                    ▼
             ResourceContract ◄── DynamicContractBuilder (from DB metadata)
                    │
        ┌───────────┼───────────────────────┐
        ▼           ▼                       ▼
   Query Engine  Validation Engine    Security Pipeline
   (filter/sort) (required/range/regex) (auth/tenant/field)
        │           │                       │
        └───────────┼───────────────────────┘
                    ▼
            CRUD Service Output
```

Two paths produce the same contract:
1. **Static:** C# attributes → `ContractBuilder` → `ResourceContract`
2. **Dynamic:** `EntityDef` / `PropertyDef` database rows → `DynamicContractBuilder` → `ResourceContract`

Once built, the contract is consumed identically by all downstream features. This means static and dynamic resources share the same validation, security, querying, and hook pipeline.

---

## Backend Routing

When both static and dynamic resources coexist, a `DataSurfaceCrudRouter` dispatches operations to the correct backend service based on the contract's `StorageBackend` field:

| Backend | Service | Storage |
|---------|---------|---------|
| `EfCore` | `EfDataSurfaceCrudService` | EF Core `DbContext` |
| `DynamicJson` | `DynamicDataSurfaceCrudService` | JSON records in metadata tables |
| `DynamicEav` | `DynamicDataSurfaceCrudService` | Entity-Attribute-Value storage |
| `DynamicHybrid` | `DynamicDataSurfaceCrudService` | Hybrid approach |

The `CompositeResourceContractProvider` merges contracts from both static and dynamic sources, ensuring unified route resolution and discovery.

---

## Next

- [Contracts](contracts.md) — The full ResourceContract schema
- [Request Lifecycle](request-lifecycle.md) — How a request flows through the pipeline
