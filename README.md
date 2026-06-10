# DataSurface

> **Contract-driven CRUD HTTP endpoints for ASP.NET Core**

DataSurface eliminates CRUD boilerplate by generating fully-featured HTTP endpoints from a single source of truth: the **ResourceContract**. Define your resources once using C# attributes or database metadata, and get automatic validation, filtering, sorting, pagination, and more.

[![Publish NuGet](https://github.com/MoslemBenDhaou/DataSurface/actions/workflows/publish-nuget.yml/badge.svg)](https://github.com/MoslemBenDhaou/DataSurface/actions/workflows/publish-nuget.yml)

You define *what a resource is* — fields, validation, security, relations — and DataSurface handles:

- CRUD endpoints
- Validation
- Filtering, sorting, pagination
- Authorization & row-level security
- Concurrency, caching, auditing, and observability

All without writing DTOs, controllers, or repetitive glue code.

### 🚫 What DataSurface Removes

- Handwritten CRUD controllers
- Read/Create/Update/Delete DTOs
- Manual validation plumbing
- Query parsing logic
- Boilerplate authorization checks
- Repeated Swagger/OpenAPI definitions

### ✅ What You Keep

- Full control over your domain model
- Strong typing
- Explicit security rules
- Override hooks when you *do* need custom logic

## Why DataSurface?

Most ASP.NET Core applications repeat the same pattern:

- Entity
- DTOs (Read / Create / Update)
- Controller
- Validation
- Query parsing
- Authorization checks

Multiply that by 20–50 entities and the cost becomes significant.

**DataSurface collapses all of that into one contract.**

You describe *what is allowed*, not *how to wire it*.

The result:
- Fewer files
- Less drift between layers
- Consistent behavior across all resources
- Faster iteration without sacrificing control

## Before vs After

### ❌ Traditional CRUD

- Entity
- 3–5 DTOs
- Controller with ~200 lines
- Manual validation
- Manual filtering & paging
- Swagger configuration
- Repeated authorization logic

```text
User.cs
UserReadDto.cs
UserCreateDto.cs
UserUpdateDto.cs
UsersController.cs
UserValidator.cs
```

### ✅ With DataSurface

- Entity
- Attributes describing the contract

```csharp
[CrudResource("users")]
public class User
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, RequiredOnCreate = true)]
    public string Email { get; set; } = default!;
}
```

```csharp
app.MapDataSurfaceCrud();
```
That’s it!

## Usage Modes

DataSurface can be used in two ways:

### 🌐 HTTP API (Most Common)

- Generates REST endpoints via Minimal APIs
- Full OpenAPI / Swagger support
- Ideal for frontend, mobile, or external integrations

```http
GET    /api/users
POST   /api/users
PATCH  /api/users/{id}
DELETE /api/users/{id}
```

### ⚙️ In-Process (No HTTP)

- Call CRUD operations directly
- Same validation, security, hooks, and contracts
- Ideal for internal services, background jobs, or modular monoliths

```csharp
await crudService.CreateAsync("User", body, ct);
```

No controllers. No HTTP. Same guarantees.

## When to Use DataSurface

✅ You build data-heavy APIs  
✅ You want consistent CRUD behavior  
✅ You want fewer DTOs and controllers  
✅ You need strong validation & security  
✅ You support dynamic or metadata-driven entities  

## When NOT to Use DataSurface

❌ You want full handcrafted controllers for every endpoint  
❌ Your API is mostly bespoke workflows, not CRUD  
❌ You dislike declarative configuration  

DataSurface is not a replacement for custom business logic —
it **handles the 80% so you can focus on the 20%**.

## Features

| Feature | Description |
|---------|-------------|
| **Auto-generated endpoints** | `GET`, `POST`, `PATCH`, `DELETE`, `PUT` via Minimal APIs |
| **Field-level control** | Choose which fields appear in read/create/update DTOs |
| **Default values** | Automatically apply defaults when creating resources |
| **Computed fields** | Server-calculated read-only fields |
| **Validation** | Required, immutable, length, range, regex, allowed values |
| **Field projection** | Select specific fields via `?fields=` query parameter |
| **Soft delete** | Built-in `ISoftDelete` convention support |
| **Timestamps** | Auto-populate `CreatedAt`/`UpdatedAt` via `ITimestamped` |
| **Filtering & Sorting** | Allowlisted fields with operators (`eq`, `gt`, `contains`, etc.) |
| **Pagination** | Built-in `page` + `pageSize` with configurable max |
| **Expansion** | `expand=relation` with depth limits |
| **HEAD support** | `HEAD` requests return count headers without body |
| **Authorization** | Per-operation policy names |
| **Row-level security** | `IResourceFilter<T>` for tenant/user-based query filtering |
| **Resource authorization** | `IResourceAuthorizer<T>` for instance-level access control |
| **Field authorization** | `IFieldAuthorizer` for field-level read/write control |
| **Tenant isolation** | Automatic multi-tenancy with `[CrudTenant]` attribute |
| **Concurrency** | Row version + `ETag` / `If-Match` headers |
| **Hooks** | Global and entity-specific lifecycle hooks |
| **Overrides** | Replace any CRUD operation with custom logic |
| **Dynamic entities** | Runtime-defined resources without recompilation |
| **Query caching** | Optional `IDistributedCache` integration |
| **Response caching** | ETag-based 304 responses, configurable Cache-Control |
| **Bulk operations** | Batch create/update/delete via `/bulk` endpoint |
| **Import/Export** | Bulk data import/export in JSON or CSV format |
| **Async streaming** | `IAsyncEnumerable` support via `/stream` endpoint |
| **Webhooks** | Publish events when CRUD operations occur |
| **Rate limiting** | ASP.NET Core rate limiting integration |
| **API key authentication** | Machine-to-machine authentication |
| **Audit logging** | `IAuditLogger` for tracking all CRUD operations |
| **Structured logging** | Built-in `ILogger` integration with operation timing |
| **Metrics** | OpenTelemetry-compatible counters and histograms |
| **Distributed tracing** | Activity/span integration for request tracing |
| **Health checks** | `IHealthCheck` implementations for monitoring |
| **Schema endpoint** | `GET /api/$schema/{resource}` returns JSON Schema |
| **API reference UI** | Swagger UI and Scalar reference over the generated OpenAPI document |
| **Feature flags** | Selectively enable/disable features with presets |

## Packages

| Package | Purpose | Download |
|---------|---------|----------|
| `DataSurface.Core` | Contracts, attributes, and builders | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.Core.svg)](https://www.nuget.org/packages/DataSurface.Core) |
| `DataSurface.EFCore` | EF Core CRUD service, hooks, query engine | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.EFCore.svg)](https://www.nuget.org/packages/DataSurface.EFCore) |
| `DataSurface.Dynamic` | Runtime metadata storage, dynamic CRUD service | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.Dynamic.svg)](https://www.nuget.org/packages/DataSurface.Dynamic) |
| `DataSurface.Http` | Minimal API endpoint mapping, query parsing, ETags | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.Http.svg)](https://www.nuget.org/packages/DataSurface.Http) |
| `DataSurface.Admin` | Admin endpoints for managing dynamic entities | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.Admin.svg)](https://www.nuget.org/packages/DataSurface.Admin) |
| `DataSurface.OpenApi` | Swashbuckle integration for typed schemas | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.OpenApi.svg)](https://www.nuget.org/packages/DataSurface.OpenApi) |
| `DataSurface.Scalar` | Scalar API reference UI (additive to Swagger) | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.Scalar.svg)](https://www.nuget.org/packages/DataSurface.Scalar) |
| `DataSurface.Generator` | *(Optional)* Source generator for typed DTOs | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.Generator.svg)](https://www.nuget.org/packages/DataSurface.Generator) |

**Typical combinations:**
- **Static only:** `Core` + `EFCore` + `Http`
- **Dynamic only:** `Core` + `Dynamic` + `Http` + `Admin`
- **Both:** All of the above

## Quick Start

### 1. Define your entity

```csharp
using DataSurface.Core.Annotations;
using DataSurface.Core.Enums;

[CrudResource("users")]
public class User
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, RequiredOnCreate = true)]
    public string Email { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public DateTime CreatedAt { get; set; }

    [CrudConcurrency]
    public byte[] RowVersion { get; set; } = default!;
}
```

### 2. Register services

```csharp
using DataSurface.EFCore.Services;
using System.Reflection;

// Register contracts, EF Core services, and the full CRUD runtime.
// The generic overload aliases your DbContext to the base DbContext
// the CRUD services depend on.
builder.Services.AddDataSurfaceEfCore<AppDbContext>(opt =>
{
    opt.AssembliesToScan = [Assembly.GetExecutingAssembly()];
});
```

### 3. Map endpoints

```csharp
using DataSurface.Http;

app.MapDataSurfaceCrud();
```

**Result:** Your API now has these endpoints:
- `GET    /api/users` — List with filtering, sorting, pagination
- `HEAD   /api/users` — Get count only (in `X-Total-Count` header)
- `GET    /api/users/{id}` — Get single resource
- `POST   /api/users` — Create
- `PATCH  /api/users/{id}` — Update
- `DELETE /api/users/{id}` — Delete
- `GET    /api/$schema/users` — Get JSON Schema for resource

## 📚 Documentation

Full documentation lives in **[`/docs`](docs/index.md)** — start there for the complete guide, or jump straight to a section below.

**Getting Started**
- [Installation](docs/getting-started/installation.md) — Package references and dependencies
- [Quick Start](docs/getting-started/quick-start.md) — Minimal working example in under 5 minutes
- [Configuration](docs/getting-started/configuration.md) — Overview of all configuration surfaces

**Architecture**
- [Overview](docs/architecture/overview.md) — Module structure, key abstractions, diagram
- [Contracts](docs/architecture/contracts.md) — The ResourceContract system in depth
- [Request Lifecycle](docs/architecture/request-lifecycle.md) — How a request flows through the pipeline

**Features**
- [CRUD Operations](docs/features/crud-operations.md) · [Querying](docs/features/querying.md) · [Validation](docs/features/validation.md) · [Relationships](docs/features/relationships.md)
- [Security](docs/features/security.md) · [Concurrency](docs/features/concurrency.md) · [Hooks & Overrides](docs/features/hooks-and-overrides.md) · [Dynamic Entities](docs/features/dynamic-entities.md)
- [Caching](docs/features/caching.md) · [Bulk & Streaming](docs/features/bulk-and-streaming.md) · [Webhooks](docs/features/webhooks.md) · [Observability](docs/features/observability.md)
- [OpenAPI Integration](docs/features/openapi.md) · [Source Generator](docs/features/source-generator.md) · [Feature Flags](docs/features/feature-flags.md)

**Reference**
- [Attributes](docs/reference/attributes.md) — All annotation attributes and their properties
- [Configuration Options](docs/reference/configuration-options.md) — All options classes
- [API Endpoints](docs/reference/api-endpoints.md) — Complete HTTP endpoint reference
- [Error Responses](docs/reference/error-responses.md) — Status codes, error types, problem details
- [Enums & Types](docs/reference/enums.md) — All enums and canonical field types

**More**
- [Benchmarks](docs/benchmarks.md) — Query engine performance analysis
- [Roadmap](docs/roadmap.md) — Feature status and planned work

## Contributing

Issues and pull requests are welcome. See the [Roadmap](docs/roadmap.md) for shipped features and what's planned next — open an issue with the `enhancement` label to suggest a feature.
