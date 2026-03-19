# DataSurface Documentation

> **Contract-driven CRUD HTTP endpoints for ASP.NET Core**

DataSurface eliminates CRUD boilerplate by generating fully-featured HTTP endpoints from a single source of truth: the **ResourceContract**. Define your resources once using C# attributes or database metadata, and get automatic validation, filtering, sorting, pagination, security, and more — without writing DTOs, controllers, or repetitive glue code.

---

## What DataSurface Does

You describe *what a resource is* — its fields, validation rules, security policies, and relationships — and DataSurface handles everything else:

- **CRUD endpoints** — `GET`, `POST`, `PATCH`, `PUT`, `DELETE`, `HEAD` via Minimal APIs
- **Validation** — Required fields, length, range, regex, allowed values
- **Querying** — Filtering, sorting, full-text search, pagination, field projection
- **Security** — Authorization policies, tenant isolation, row-level security, field-level access control
- **Concurrency** — Optimistic concurrency via ETags and row versions
- **Extensibility** — Lifecycle hooks, operation overrides, webhooks
- **Observability** — Structured logging, OpenTelemetry metrics, distributed tracing, audit logging
- **Dynamic entities** — Runtime-defined resources without recompilation

### What It Removes

- Handwritten CRUD controllers
- Read / Create / Update / Delete DTOs
- Manual validation plumbing
- Query parsing logic
- Boilerplate authorization checks
- Repeated Swagger / OpenAPI definitions

### What You Keep

- Full control over your domain model
- Strong typing
- Explicit security rules
- Override hooks when you need custom logic

---

## When to Use DataSurface

**Good fit:**
- Data-heavy APIs with many CRUD resources
- Consistent behavior needed across all resources
- Fewer DTOs and controllers desired
- Strong validation and security requirements
- Dynamic or metadata-driven entities

**Not a fit:**
- Fully handcrafted controllers for every endpoint
- APIs that are mostly bespoke workflows, not CRUD
- Teams that dislike declarative configuration

DataSurface handles the 80% so you can focus on the 20% that requires custom logic.

---

## Before vs After

### Traditional CRUD — per entity

```
User.cs
UserReadDto.cs
UserCreateDto.cs
UserUpdateDto.cs
UsersController.cs
UserValidator.cs
```

### With DataSurface — per entity

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

Multiply the savings by 20–50 entities and the cost difference becomes significant.

---

## Usage Modes

### HTTP API (Most Common)

Generates REST endpoints via Minimal APIs with full OpenAPI / Swagger support. Ideal for frontend, mobile, or external integrations.

```http
GET    /api/users
POST   /api/users
PATCH  /api/users/{id}
DELETE /api/users/{id}
```

### In-Process (No HTTP)

Call CRUD operations directly via `IDataSurfaceCrudService`. Same validation, security, hooks, and contracts — no HTTP overhead. Ideal for internal services, background jobs, or modular monoliths.

```csharp
await crudService.CreateAsync("User", body, ct);
```

---

## Packages

| Package | Purpose |
|---------|---------|
| `DataSurface.Core` | Contracts, attributes, enums, and contract builders |
| `DataSurface.EFCore` | EF Core CRUD service, hooks, query engine, mapper |
| `DataSurface.Http` | Minimal API endpoint mapping, query parsing, ETags |
| `DataSurface.Dynamic` | Runtime metadata storage, dynamic CRUD service |
| `DataSurface.Admin` | Admin REST API for managing dynamic entity definitions |
| `DataSurface.OpenApi` | Swashbuckle integration for typed schemas |
| `DataSurface.Generator` | *(Optional)* Source generator for typed DTOs |

**Typical combinations:**
- **Static only:** `Core` + `EFCore` + `Http`
- **Dynamic only:** `Core` + `Dynamic` + `Http` + `Admin`
- **Both:** All of the above

---

## Documentation Map

### Getting Started

- [Installation](getting-started/installation.md) — Package references and dependencies
- [Quick Start](getting-started/quick-start.md) — Minimal working example in under 5 minutes
- [Configuration](getting-started/configuration.md) — Overview of all configuration surfaces

### Architecture

- [Overview](architecture/overview.md) — Module structure, key abstractions, architecture diagram
- [Contracts](architecture/contracts.md) — The ResourceContract system in depth
- [Request Lifecycle](architecture/request-lifecycle.md) — How a request flows through the pipeline

### Features

- [CRUD Operations](features/crud-operations.md) — Endpoints, PUT vs PATCH, operation control
- [Querying](features/querying.md) — Filtering, sorting, pagination, search, field projection
- [Validation](features/validation.md) — Field validation rules and error responses
- [Relationships](features/relationships.md) — Relations, expansion, write modes
- [Security](features/security.md) — Authorization, tenant isolation, row-level security, field-level access, API keys
- [Concurrency](features/concurrency.md) — ETags, If-Match, optimistic concurrency
- [Hooks & Overrides](features/hooks-and-overrides.md) — Lifecycle hooks and operation overrides
- [Dynamic Entities](features/dynamic-entities.md) — Runtime-defined resources and admin API
- [Caching](features/caching.md) — Query caching and response caching
- [Bulk Operations & Streaming](features/bulk-and-streaming.md) — Batch operations, streaming, import/export
- [Webhooks](features/webhooks.md) — Event publishing on CRUD operations
- [Observability](features/observability.md) — Logging, metrics, tracing, health checks, audit logging
- [OpenAPI Integration](features/openapi.md) — Swagger schemas and schema endpoint
- [Source Generator](features/source-generator.md) — Typed DTO code generation
- [Feature Flags](features/feature-flags.md) — Selective feature enablement with presets

### Reference

- [Attributes](reference/attributes.md) — All annotation attributes and their properties
- [Configuration Options](reference/configuration-options.md) — All options classes
- [API Endpoints](reference/api-endpoints.md) — Complete HTTP endpoint reference
- [Error Responses](reference/error-responses.md) — Status codes, error types, problem details format
- [Enums & Types](reference/enums.md) — All enums and canonical field types

### Other

- [Benchmarks](benchmarks.md) — Query engine performance analysis
- [Roadmap](roadmap.md) — Feature implementation status and planned work
