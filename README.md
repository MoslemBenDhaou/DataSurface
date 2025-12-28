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
await crudService.CreateAsync("User", body, context, ct);
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

## Table of Contents

- [Features](#features)
- [Packages](#packages)
- [Quick Start](#quick-start)
- [Guides](#guides)
  - [Static Resources (EF Core)](#guide-static-resources-ef-core)
  - [Dynamic Resources (Runtime Metadata)](#guide-dynamic-resources-runtime-metadata)
  - [Admin Endpoints](#guide-admin-endpoints)
  - [OpenAPI / Swagger](#guide-openapi--swagger)
- [Feature Details](#auto-generated-endpoints)
  - [Auto-generated Endpoints](#auto-generated-endpoints)
  - [Field-level Control](#field-level-control)
  - [Default Values](#default-values)
  - [Computed Fields](#computed-fields)
  - [Validation](#validation)
  - [Field Projection](#field-projection)
  - [Soft Delete](#soft-delete)
  - [Timestamps](#timestamps)
  - [Filtering & Sorting](#filtering--sorting)
  - [Pagination](#pagination)
  - [Expansion](#expansion)
  - [HEAD Support](#head-support)
  - [Authorization](#authorization)
  - [Row-level Security](#row-level-security)
  - [Resource Authorization](#resource-authorization)
  - [Field Authorization](#field-authorization)
  - [Tenant Isolation](#tenant-isolation)
  - [Concurrency](#concurrency)
  - [Hooks](#hooks)
  - [Overrides](#overrides)
  - [Dynamic Entities](#dynamic-entities)
  - [Compiled Queries](#compiled-queries)
  - [Query Caching](#query-caching)
  - [Response Caching](#response-caching)
  - [Bulk Operations](#bulk-operations)
  - [Import/Export](#importexport)
  - [Async Streaming](#async-streaming)
  - [Webhooks](#webhooks)
  - [Rate Limiting](#rate-limiting)
  - [API Key Authentication](#api-key-authentication)
  - [Audit Logging](#audit-logging)
  - [Structured Logging](#structured-logging)
  - [Metrics](#metrics)
  - [Distributed Tracing](#distributed-tracing)
  - [Health Checks](#health-checks)
  - [Schema Endpoint](#schema-endpoint)
- [Attributes Reference](#attributes-reference)
- [Configuration Options](#configuration-options)
  - [DataSurfaceEfCoreOptions](#datasurfaceefcoreoptions)
  - [DataSurfaceHttpOptions](#datasurfacehttpoptions)
  - [DataSurfaceDynamicOptions](#datasurfacedynamicoptions)
  - [DataSurfaceAdminOptions](#datasurfaceadminoptions)
  - [Feature Flags](#feature-flags)
- [Architecture](#architecture)
- [Quick Checklist](#quick-checklist)
- [Planned Features](#planned-features)

## Features

| Feature | Description |
|---------|-------------|
| [**Auto-generated endpoints**](#auto-generated-endpoints) | `GET`, `POST`, `PATCH`, `DELETE`, `PUT` via Minimal APIs |
| [**Field-level control**](#field-level-control) | Choose which fields appear in read/create/update DTOs |
| [**Default values**](#default-values) | Automatically apply defaults when creating resources |
| [**Computed fields**](#computed-fields) | Server-calculated read-only fields |
| [**Validation**](#validation) | Required, immutable, length, range, regex, allowed values |
| [**Field projection**](#field-projection) | Select specific fields via `?fields=` query parameter |
| [**Soft delete**](#soft-delete) | Built-in `ISoftDelete` convention support |
| [**Timestamps**](#timestamps) | Auto-populate `CreatedAt`/`UpdatedAt` via `ITimestamped` |
| [**Filtering & Sorting**](#filtering--sorting) | Allowlisted fields with operators (`eq`, `gt`, `contains`, etc.) |
| [**Pagination**](#pagination) | Built-in `page` + `pageSize` with configurable max |
| [**Expansion**](#expansion) | `expand=relation` with depth limits |
| [**HEAD support**](#head-support) | `HEAD` requests return count headers without body |
| [**Authorization**](#authorization) | Per-operation policy names |
| [**Row-level security**](#row-level-security) | `IResourceFilter<T>` for tenant/user-based query filtering |
| [**Resource authorization**](#resource-authorization) | `IResourceAuthorizer<T>` for instance-level access control |
| [**Field authorization**](#field-authorization) | `IFieldAuthorizer` for field-level read/write control |
| [**Tenant isolation**](#tenant-isolation) | Automatic multi-tenancy with `[CrudTenant]` attribute |
| [**Concurrency**](#concurrency) | Row version + `ETag` / `If-Match` headers |
| [**Hooks**](#hooks) | Global and entity-specific lifecycle hooks |
| [**Overrides**](#overrides) | Replace any CRUD operation with custom logic |
| [**Dynamic entities**](#dynamic-entities) | Runtime-defined resources without recompilation |
| [**Compiled queries**](#compiled-queries) | Pre-compiled EF Core queries for common operations |
| [**Query caching**](#query-caching) | Optional `IDistributedCache` integration |
| [**Response caching**](#response-caching) | ETag-based 304 responses, configurable Cache-Control |
| [**Bulk operations**](#bulk-operations) | Batch create/update/delete via `/bulk` endpoint |
| [**Import/Export**](#importexport) | Bulk data import/export in JSON or CSV format |
| [**Async streaming**](#async-streaming) | `IAsyncEnumerable` support via `/stream` endpoint |
| [**Webhooks**](#webhooks) | Publish events when CRUD operations occur |
| [**Rate limiting**](#rate-limiting) | ASP.NET Core rate limiting integration |
| [**API key authentication**](#api-key-authentication) | Machine-to-machine authentication |
| [**Audit logging**](#audit-logging) | `IAuditLogger` for tracking all CRUD operations |
| [**Structured logging**](#structured-logging) | Built-in `ILogger` integration with operation timing |
| [**Metrics**](#metrics) | OpenTelemetry-compatible counters and histograms |
| [**Distributed tracing**](#distributed-tracing) | Activity/span integration for request tracing |
| [**Health checks**](#health-checks) | `IHealthCheck` implementations for monitoring |
| [**Schema endpoint**](#schema-endpoint) | `GET /api/$schema/{resource}` returns JSON Schema |
| [**Feature flags**](#feature-flags) | Selectively enable/disable features with presets |

## Packages

| Package | Purpose | Download |
|---------|---------|----------|
| `DataSurface.Core` | Contracts, attributes, and builders | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.Core.svg)](https://www.nuget.org/packages/DataSurface.Core) |
| `DataSurface.EFCore` | EF Core CRUD service, hooks, query engine | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.EFCore.svg)](https://www.nuget.org/packages/DataSurface.EFCore) |
| `DataSurface.Dynamic` | Runtime metadata storage, dynamic CRUD service | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.Dynamic.svg)](https://www.nuget.org/packages/DataSurface.Dynamic) |
| `DataSurface.Http` | Minimal API endpoint mapping, query parsing, ETags | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.Http.svg)](https://www.nuget.org/packages/DataSurface.Http) |
| `DataSurface.Admin` | Admin endpoints for managing dynamic entities | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.Admin.svg)](https://www.nuget.org/packages/DataSurface.Admin) |
| `DataSurface.OpenApi` | Swashbuckle integration for typed schemas | [![NuGet Downloads](https://img.shields.io/nuget/v/DataSurface.OpenApi.svg)](https://www.nuget.org/packages/DataSurface.OpenApi) |
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

// Register contracts and EF Core services
builder.Services.AddDataSurfaceEfCore(opt =>
{
    opt.AssembliesToScan = [Assembly.GetExecutingAssembly()];
});

// Register CRUD runtime
builder.Services.AddScoped<CrudHookDispatcher>();
builder.Services.AddSingleton<CrudOverrideRegistry>();
builder.Services.AddScoped<EfDataSurfaceCrudService>();
builder.Services.AddScoped<IDataSurfaceCrudService>(sp => 
    sp.GetRequiredService<EfDataSurfaceCrudService>());
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

## Guides

### Guide: Static Resources (EF Core)

For compile-time defined entities backed by Entity Framework Core.

#### Step 1: Annotate your entities

```csharp
[CrudResource("posts", MaxPageSize = 100)]
public class Post
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort,
        RequiredOnCreate = true, MaxLength = 200)]
    public string Title { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
    public string? Content { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Filter)]
    public int AuthorId { get; set; }

    [CrudField(CrudDto.Read)]
    public DateTime CreatedAt { get; set; }

    [CrudRelation(ReadExpandAllowed = true, WriteMode = RelationWriteMode.ById)]
    public User Author { get; set; } = default!;

    [CrudConcurrency]
    public byte[] RowVersion { get; set; } = default!;
}
```

#### Step 2: Create your DbContext

```csharp
using DataSurface.EFCore.Context;

public class AppDbContext : DeclarativeDbContext<AppDbContext>
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        DataSurfaceEfCoreOptions dsOptions,
        IResourceContractProvider contracts)
        : base(options, dsOptions, contracts) { }
}
```

#### Step 3: Register all services

```csharp
// Program.cs
using DataSurface.EFCore.Services;
using DataSurface.Http;

var builder = WebApplication.CreateBuilder(args);

// EF Core
builder.Services.AddDbContext<AppDbContext>(opt => 
    opt.UseSqlServer(connectionString));

// DataSurface contracts
builder.Services.AddDataSurfaceEfCore(opt =>
{
    opt.AssembliesToScan = [typeof(Program).Assembly];
});

// DataSurface runtime
builder.Services.AddScoped<CrudHookDispatcher>();
builder.Services.AddSingleton<CrudOverrideRegistry>();
builder.Services.AddScoped<EfDataSurfaceCrudService>();
builder.Services.AddScoped<IDataSurfaceCrudService>(sp => 
    sp.GetRequiredService<EfDataSurfaceCrudService>());

var app = builder.Build();

// Map CRUD endpoints
app.MapDataSurfaceCrud();

app.Run();
```

---

### Guide: Dynamic Resources (Runtime Metadata)

For entities defined at runtime without recompilation.

#### Step 1: Add dynamic tables to your DbContext

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.AddDataSurfaceDynamic(schema: "dbo");
}
```

#### Step 2: Register dynamic services

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

#### Step 3: Map with dynamic catch-all enabled

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    MapStaticResources = true,
    MapDynamicCatchAll = true  // Enables /api/d/{route}
});
```

---

### Guide: Admin Endpoints

Manage dynamic entity definitions via REST API.

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

**Available endpoints:**

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/admin/ds/entities` | List all entity definitions |
| `GET` | `/admin/ds/entities/{key}` | Get single entity definition |
| `PUT` | `/admin/ds/entities/{key}` | Create or update entity definition |
| `DELETE` | `/admin/ds/entities/{key}` | Delete entity definition |
| `GET` | `/admin/ds/export` | Export all definitions as JSON |
| `POST` | `/admin/ds/import` | Import definitions from JSON |
| `POST` | `/admin/ds/entities/{key}/reindex` | Rebuild search indexes |

---

### Guide: OpenAPI / Swagger

Generate typed schemas for Swashbuckle.

```csharp
using DataSurface.OpenApi;

builder.Services.AddSwaggerGen(swagger =>
{
    builder.Services.AddDataSurfaceOpenApi(swagger);
});
```

This adds:
- Typed request/response schemas per resource
- Query parameter documentation for filtering
- Proper `PagedResult<T>` schema for list responses

## Auto-generated Endpoints

DataSurface generates fully-featured REST endpoints via Minimal APIs:

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/{resource}` | List with filtering, sorting, pagination |
| `HEAD` | `/api/{resource}` | Get count only (in `X-Total-Count` header) |
| `GET` | `/api/{resource}/{id}` | Get single resource |
| `POST` | `/api/{resource}` | Create new resource |
| `PATCH` | `/api/{resource}/{id}` | Partial update (only provided fields) |
| `PUT` | `/api/{resource}/{id}` | Full replacement (all fields required) |
| `DELETE` | `/api/{resource}/{id}` | Delete resource |
| `GET` | `/api/$schema/{resource}` | Get JSON Schema for resource |

**PUT vs PATCH:**
- **PATCH** — Partial update: only fields in the request body are modified
- **PUT** — Full replacement: all updatable fields must be provided (returns 400 if any are missing)

To enable PUT endpoints:
```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnablePutForFullUpdate = true
});
```

See [Quick Start](#quick-start) for setup instructions.

---

## Field-level Control

Control which fields appear in read/create/update DTOs using the `[CrudField]` attribute with `CrudDto` flags.

```csharp
[CrudResource("products")]
public class Product
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, RequiredOnCreate = true)]
    public string Name { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Create)]  // Can set on create, but not update
    public string SKU { get; set; } = default!;

    [CrudField(CrudDto.Read)]  // Read-only, never in request bodies
    public DateTime CreatedAt { get; set; }

    // No attribute = not exposed via API
    internal string InternalNotes { get; set; } = default!;
}
```

See [`[CrudField]`](#crudfield) in Attributes Reference for full details.

---

## Default Values

Automatically apply default values when creating resources. Defaults are applied server-side when a field is not provided in the request body:

```csharp
[CrudResource("orders")]
public class Order
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create, DefaultValue = "pending")]
    public string Status { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Create, DefaultValue = 0)]
    public int Priority { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create, DefaultValue = false)]
    public bool IsUrgent { get; set; }
}
```

**Behavior:**
- Defaults are only applied on **create** operations (POST)
- If a field is provided in the request, the provided value is used
- If a field is omitted, the `DefaultValue` is applied
- Works with strings, numbers, booleans, and other primitive types

---

## Computed Fields

Define server-calculated read-only fields that are evaluated at read time. Computed fields are never stored in the database—they're calculated dynamically based on other field values:

```csharp
[CrudResource("employees")]
public class Employee
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
    public string FirstName { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
    public string LastName { get; set; } = default!;

    [CrudField(CrudDto.Read, ComputedExpression = "FirstName + ' ' + LastName")]
    public string FullName { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Create)]
    public decimal Salary { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create)]
    public decimal Bonus { get; set; }

    [CrudField(CrudDto.Read, ComputedExpression = "Salary + Bonus")]
    public decimal TotalCompensation { get; set; }
}
```

**Supported Expressions:**
- String concatenation: `"FirstName + ' ' + LastName"`
- Numeric operations: `"Salary + Bonus"`, `"Price * Quantity"`
- Property references: Direct property names like `"PropertyName"`

**Notes:**
- Computed fields are **read-only**—they cannot be set via POST or PATCH
- Values are calculated fresh on every read operation
- The expression references CLR property names (not API names)

---

## Validation

DataSurface provides comprehensive built-in validation via `[CrudField]` attributes:

```csharp
[CrudResource("users")]
public class User
{
    [CrudKey]
    public int Id { get; set; }

    // Required on create
    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, RequiredOnCreate = true)]
    public string Email { get; set; } = default!;

    // Immutable after creation
    [CrudField(CrudDto.Read | CrudDto.Create, Immutable = true)]
    public string Username { get; set; } = default!;

    // String length validation
    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, MinLength = 8, MaxLength = 100)]
    public string Password { get; set; } = default!;

    // Numeric range validation
    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, Min = 0, Max = 150)]
    public int Age { get; set; }

    // Regex pattern validation
    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, Regex = @"^\+?[1-9]\d{1,14}$")]
    public string? PhoneNumber { get; set; }

    // Allowed values (enum-like validation)
    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, AllowedValues = "Active|Inactive|Pending")]
    public string Status { get; set; } = default!;
}
```

**Validation Rules:**

| Rule | Description |
|------|-------------|
| `RequiredOnCreate` | Field must be present on POST requests |
| `Immutable` | Field rejected on PATCH requests (can only be set on create) |
| `MinLength` | Minimum string length |
| `MaxLength` | Maximum string length |
| `Min` | Minimum numeric value |
| `Max` | Maximum numeric value |
| `Regex` | Regular expression pattern the value must match |
| `AllowedValues` | Pipe-separated list of valid values |

**Additional Behavior:**
- Unknown fields in request bodies are automatically rejected
- Validation errors return HTTP 400 with detailed problem details

---

## Field Projection

Select specific fields to return using the `?fields=` query parameter. This reduces payload size and improves performance:

```http
GET /api/users?fields=id,email,name
```

**Response:**
```json
{
  "items": [
    { "id": 1, "email": "john@example.com", "name": "John" },
    { "id": 2, "email": "jane@example.com", "name": "Jane" }
  ]
}
```

**Usage:**
- Comma-separated list of field API names
- Only requested fields are included in the response
- Invalid field names are ignored
- Works with list (`GET /api/resource`) and single (`GET /api/resource/{id}`) endpoints

---

## Soft Delete

Entities implementing `ISoftDelete` are automatically filtered instead of permanently deleted:

```csharp
using DataSurface.EFCore.Interfaces;

public class User : ISoftDelete
{
    public int Id { get; set; }
    public string Email { get; set; } = default!;
    
    // Automatically set to true on DELETE, filtered from queries
    public bool IsDeleted { get; set; }
}
```

- **On delete:** `IsDeleted` is set to `true` instead of removing the row
- **On queries:** Soft-deleted records are automatically filtered out
- **Control:** Disable via `EnableSoftDeleteFilter = false` in options

---

## Timestamps

Entities implementing `ITimestamped` get automatic timestamp population:

```csharp
using DataSurface.EFCore.Interfaces;

public class User : ITimestamped
{
    public int Id { get; set; }
    public string Email { get; set; } = default!;
    
    // Auto-populated by DeclarativeDbContext
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

- **On insert:** Both `CreatedAt` and `UpdatedAt` are set to `DateTime.UtcNow`
- **On update:** Only `UpdatedAt` is refreshed
- **Control:** Disable via `EnableTimestampConvention = false` in options

---

## Filtering & Sorting

### Filter operators

```
?filter[price]=100          # equals (default)
?filter[price]=eq:100       # equals
?filter[price]=neq:100      # not equals
?filter[price]=gt:100       # greater than
?filter[price]=gte:100      # greater than or equal
?filter[price]=lt:100       # less than
?filter[price]=lte:100      # less than or equal
?filter[name]=contains:john # string contains
?filter[name]=starts:john   # string starts with
?filter[name]=ends:son      # string ends with
?filter[status]=in:a|b|c    # in list (pipe-separated)
?filter[email]=isnull:true  # is null
?filter[email]=isnull:false # is not null
```

### Full-Text Search

Search across all searchable fields using the `q` parameter:

```
?q=john                     # searches all fields marked with Searchable = true
```

Mark fields as searchable:

```csharp
[CrudField(CrudDto.Read | CrudDto.Filter, Searchable = true)]
public string Title { get; set; }

[CrudField(CrudDto.Read | CrudDto.Filter, Searchable = true)]
public string Description { get; set; }
```

### Field Projection

Return only specific fields using the `fields` parameter:

```
?fields=id,title,createdAt  # only return these fields
```

### Sorting

```
?sort=title,-createdAt      # Comma-separated, `-` prefix for descending
```

Fields must have `CrudDto.Filter` or `CrudDto.Sort` flags to be filterable/sortable.

---

## Pagination

| Parameter | Example | Description |
|-----------|---------|-------------|
| `page` | `?page=2` | Page number (1-based, default: 1) |
| `pageSize` | `?pageSize=50` | Items per page (default: 20) |

### Response format

```json
{
  "items": [...],
  "page": 1,
  "pageSize": 20,
  "total": 142
}
```

**Response headers (on list endpoints):**
```http
X-Total-Count: 142
X-Page: 1
X-Page-Size: 20
```

Maximum page size is configurable via `MaxPageSize` on `[CrudResource]`.

---

## Expansion

Include related resources using the `expand` parameter:

```
?expand=author,tags
```

Relations must have `ReadExpandAllowed = true` in `[CrudRelation]` to be expandable. Maximum expansion depth is configurable via `MaxExpandDepth` on `[CrudResource]`.

---

## HEAD Support

Use `HEAD` to get only the count without fetching data:

```http
HEAD /api/users?filter[status]=active
```

**Response:**
```http
HTTP/1.1 200 OK
X-Total-Count: 42
X-Page: 1
X-Page-Size: 200
```

---

## Authorization

Set authorization policies per operation using `[CrudAuthorize]`:

```csharp
[CrudAuthorize(Policy = "AdminOnly")]  // All operations
[CrudAuthorize(Operation = CrudOperation.Delete, Policy = "SuperAdmin")]
public class User { }
```

See [`[CrudAuthorize]`](#crudauthorize) in Attributes Reference for full details.

---

## Row-level Security

Filter queries based on user context using `IResourceFilter<T>`:

```csharp
using DataSurface.EFCore.Interfaces;

public class TenantResourceFilter : IResourceFilter<Order>
{
    private readonly ITenantContext _tenant;
    
    public TenantResourceFilter(ITenantContext tenant) => _tenant = tenant;
    
    public Expression<Func<Order, bool>>? GetFilter(ResourceContract contract)
        => o => o.TenantId == _tenant.TenantId;
}

// Register
builder.Services.AddScoped<IResourceFilter<Order>, TenantResourceFilter>();
```

- **Automatic application:** Filters apply to List, Get, Update, and Delete operations
- **Security guarantee:** Users can only access records matching the filter
- **Non-generic option:** Implement `IResourceFilter` for dynamic type filtering

---

## Resource Authorization

Authorize access to specific resource instances using `IResourceAuthorizer<T>`:

```csharp
using DataSurface.EFCore.Interfaces;

public class OrderAuthorizer : IResourceAuthorizer<Order>
{
    private readonly IHttpContextAccessor _http;
    
    public OrderAuthorizer(IHttpContextAccessor http) => _http = http;
    
    public Task<AuthorizationResult> AuthorizeAsync(
        ResourceContract contract,
        Order? entity,
        CrudOperation operation,
        CancellationToken ct)
    {
        var userId = _http.HttpContext?.User.FindFirst("sub")?.Value;
        
        // Owner can do anything with their orders
        if (entity?.OwnerId == userId)
            return Task.FromResult(AuthorizationResult.Success());
        
        // Admins can access all orders
        if (_http.HttpContext?.User.IsInRole("Admin") == true)
            return Task.FromResult(AuthorizationResult.Success());
        
        return Task.FromResult(AuthorizationResult.Fail("You can only access your own orders."));
    }
}

// Register
builder.Services.AddScoped<IResourceAuthorizer<Order>, OrderAuthorizer>();
```

**Integration with ASP.NET Core Authorization:**

```csharp
public class PolicyResourceAuthorizer : IResourceAuthorizer
{
    private readonly IAuthorizationService _auth;
    private readonly IHttpContextAccessor _http;
    
    public PolicyResourceAuthorizer(IAuthorizationService auth, IHttpContextAccessor http)
    {
        _auth = auth;
        _http = http;
    }
    
    public async Task<AuthorizationResult> AuthorizeAsync(
        ResourceContract contract,
        object? entity,
        CrudOperation operation,
        CancellationToken ct)
    {
        var user = _http.HttpContext?.User;
        if (user is null)
            return AuthorizationResult.Fail("No authenticated user.");
        
        // Use ASP.NET Core policy-based authorization with resource
        var policyName = $"{contract.ResourceKey}.{operation}";
        var result = await _auth.AuthorizeAsync(user, entity, policyName);
        
        return result.Succeeded 
            ? AuthorizationResult.Success() 
            : AuthorizationResult.Fail("Access denied by policy.");
    }
}

// Register
builder.Services.AddScoped<IResourceAuthorizer, PolicyResourceAuthorizer>();
```

- **Instance-level checks:** "Can this user access Order #123?"
- **Operation-specific:** Different rules for Get vs Update vs Delete
- **Typed and non-generic:** Use `IResourceAuthorizer<T>` for compile-time safety or `IResourceAuthorizer` for global policies
- **Integrates with ASP.NET Core:** Leverage existing `IAuthorizationService` and policies

---

## Field Authorization

Control which fields users can read or write using `IFieldAuthorizer`:

```csharp
using DataSurface.EFCore.Interfaces;

public class SensitiveFieldAuthorizer : IFieldAuthorizer
{
    private readonly IHttpContextAccessor _http;
    
    public SensitiveFieldAuthorizer(IHttpContextAccessor http) => _http = http;
    
    public bool CanReadField(ResourceContract contract, string fieldName)
    {
        if (fieldName == "salary")
            return _http.HttpContext?.User.IsInRole("HR") ?? false;
        return true;
    }
    
    public bool CanWriteField(ResourceContract contract, string fieldName, CrudOperation op)
    {
        if (fieldName == "isAdmin")
            return _http.HttpContext?.User.IsInRole("Admin") ?? false;
        return true;
    }
}

// Register
builder.Services.AddScoped<IFieldAuthorizer, SensitiveFieldAuthorizer>();
```

- **Read redaction:** Unauthorized fields are removed from responses
- **Write validation:** Unauthorized field writes throw `UnauthorizedAccessException`

---

## Tenant Isolation

Implement automatic multi-tenancy with the `[CrudTenant]` attribute. Tenant isolation ensures users can only access data belonging to their tenant:

```csharp
[CrudResource("orders")]
public class Order
{
    [CrudKey]
    public int Id { get; set; }

    [CrudTenant(ClaimType = "tenant_id", Required = true)]
    public string TenantId { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
    public string ProductName { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Create)]
    public decimal Amount { get; set; }
}
```

**Behavior:**
- **On queries:** Automatically filters results to only include records matching the user's tenant claim
- **On create:** Automatically sets the tenant field to the user's tenant claim value
- **On update/delete:** Validates the resource belongs to the user's tenant

**Configuration Options:**

| Option | Description |
|--------|-------------|
| `ClaimType` | The claim type to extract tenant ID from (e.g., `"tenant_id"`, `"org_id"`) |
| `Required` | If `true`, requests without the tenant claim are rejected with 401 |

**Custom Tenant Resolution:**

For advanced scenarios, implement `ITenantResolver`:

```csharp
public class CustomTenantResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _http;
    
    public CustomTenantResolver(IHttpContextAccessor http) => _http = http;
    
    public string? ResolveTenantId(TenantContract tenant)
    {
        // Custom logic: header, subdomain, database lookup, etc.
        return _http.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    }
}

// Register
builder.Services.AddScoped<ITenantResolver, CustomTenantResolver>();
```

---

## Concurrency

Row version fields enable optimistic concurrency via ETag headers.

**Response:**
```http
HTTP/1.1 200 OK
ETag: W/"AAAAAAB="
```

**Update with concurrency check:**
```http
PATCH /api/users/1
If-Match: W/"AAAAAAB="
Content-Type: application/json

{"email": "new@example.com"}
```

See [`[CrudConcurrency]`](#crudconcurrency) in Attributes Reference for configuration.

---

## Hooks

### Global Hooks

Run for all resources.

```csharp
public class AuditHook : ICrudHook
{
    public int Order => 0;  // Lower runs first

    public Task BeforeAsync(CrudHookContext ctx)
    {
        Console.WriteLine($"Before {ctx.Operation} on {ctx.Contract.ResourceKey}");
        return Task.CompletedTask;
    }

    public Task AfterAsync(CrudHookContext ctx)
    {
        Console.WriteLine($"After {ctx.Operation}");
        return Task.CompletedTask;
    }
}

// Register
builder.Services.AddScoped<ICrudHook, AuditHook>();
```

### Entity-Specific Hooks

Run only for a specific entity type.

```csharp
public class UserHook : ICrudHook<User>
{
    public int Order => 0;

    public Task BeforeCreateAsync(User entity, JsonObject body, CrudHookContext ctx)
    {
        entity.CreatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task AfterCreateAsync(User entity, CrudHookContext ctx)
    {
        // Send welcome email
        return Task.CompletedTask;
    }
}

// Register
builder.Services.AddScoped<ICrudHook<User>, UserHook>();
```

---

## Overrides

Completely replace CRUD logic for a resource.

```csharp
var registry = app.Services.GetRequiredService<CrudOverrideRegistry>();

registry.Override("User", CrudOperation.Create, 
    async (CreateOverride)((contract, body, ctx, ct) =>
    {
        // Custom creation logic
        var user = new User { Email = body["email"]!.GetValue<string>() };
        ctx.Db.Add(user);
        await ctx.Db.SaveChangesAsync(ct);
        
        return new JsonObject { ["id"] = user.Id, ["email"] = user.Email };
    }));
```

---

## Dynamic Entities

Runtime-defined resources without recompilation. See [Guide: Dynamic Resources](#guide-dynamic-resources-runtime-metadata) for full setup instructions.

---

## Compiled Queries

Pre-compiled EF Core queries for common operations:

```csharp
builder.Services.AddSingleton<CompiledQueryCache>();

// Usage in custom code
var cache = sp.GetRequiredService<CompiledQueryCache>();
var findById = cache.GetOrCreateFindByIdQuery<User, int>("Id");
var user = findById(dbContext, 5);
```

---

## Query Caching

Cache query results using `IDistributedCache`:

```csharp
// Add Redis cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});

// Configure DataSurface caching
builder.Services.Configure<DataSurfaceCacheOptions>(options =>
{
    options.EnableQueryCaching = true;
    options.DefaultCacheDuration = TimeSpan.FromMinutes(5);
    options.ResourceConfigs["Product"] = new ResourceCacheConfig
    {
        Duration = TimeSpan.FromMinutes(30),
        CacheList = true,
        CacheGet = true
    };
});

builder.Services.AddSingleton<IQueryResultCache, DistributedQueryResultCache>();
```

---

## Response Caching

Enable ETag-based conditional GET (304 Not Modified) and Cache-Control headers:

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableConditionalGet = true,      // If-None-Match → 304 response
    CacheControlMaxAgeSeconds = 300   // Cache-Control: max-age=300
});
```

Clients can cache responses and send `If-None-Match` headers to receive 304 responses when data hasn't changed.

---

## Bulk Operations

Batch create, update, and delete operations via `POST /api/{resource}/bulk`:

```json
{
  "create": [
    { "name": "User 1", "email": "user1@example.com" },
    { "name": "User 2", "email": "user2@example.com" }
  ],
  "update": [
    { "id": 5, "patch": { "name": "Updated Name" } }
  ],
  "delete": [10, 11, 12],
  "stopOnError": true,
  "useTransaction": true
}
```

Register the bulk service:

```csharp
builder.Services.AddScoped<IDataSurfaceBulkService, EfDataSurfaceBulkService>();
```

---

## Async Streaming

Stream large datasets via `GET /api/{resource}/stream` (NDJSON format):

```csharp
// Register streaming service
builder.Services.AddScoped<IDataSurfaceStreamingService, EfDataSurfaceStreamingService>();

// Client usage
await foreach (var item in streamingService.StreamAsync("User", spec))
{
    // Process each item as it arrives
}
```

Response format (newline-delimited JSON):
```
{"id":1,"name":"User 1"}
{"id":2,"name":"User 2"}
{"id":3,"name":"User 3"}
```

---

## Import/Export

Bulk data import and export via dedicated endpoints:

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableImportExport = true
});
```

**Export Endpoint:**
```http
GET /api/users/export?format=json
GET /api/users/export?format=csv
```

- Exports all records (respecting query filters and security)
- Supports JSON and CSV formats
- CSV format includes headers matching API field names

**Import Endpoint:**
```http
POST /api/users/import
Content-Type: application/json

[
  { "email": "user1@example.com", "name": "User 1" },
  { "email": "user2@example.com", "name": "User 2" }
]
```

- Imports an array of records
- Each record is validated against the resource contract
- Returns summary of imported, failed, and skipped records

---

## Webhooks

Publish events when CRUD operations occur. Useful for integrations, audit trails, and event-driven architectures:

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableWebhooks = true
});
```

Implement and register a webhook publisher:

```csharp
using DataSurface.Core.Webhooks;

public class MyWebhookPublisher : IWebhookPublisher
{
    private readonly HttpClient _http;
    private readonly ILogger<MyWebhookPublisher> _logger;
    
    public MyWebhookPublisher(HttpClient http, ILogger<MyWebhookPublisher> logger)
    {
        _http = http;
        _logger = logger;
    }
    
    public async Task PublishAsync(WebhookEvent evt, CancellationToken ct)
    {
        _logger.LogInformation("Webhook: {Operation} on {Resource} id={Id}", 
            evt.Operation, evt.ResourceKey, evt.EntityId);
        
        // Send to external endpoint
        await _http.PostAsJsonAsync("https://hooks.example.com/datasurface", evt, ct);
    }
}

// Register
builder.Services.AddSingleton<IWebhookPublisher, MyWebhookPublisher>();
```

**`WebhookEvent` properties:**
- `Operation` — Create, Update, or Delete
- `ResourceKey` — The resource that changed
- `EntityId` — ID of the affected entity
- `Timestamp` — UTC timestamp
- `Payload` — JSON representation of the entity (for create/update)

**Failure Handling:**
- Webhook publishing is fire-and-forget by default
- Failures are logged but don't fail the CRUD operation
- Implement retry logic in your `IWebhookPublisher` if needed

---

## Rate Limiting

Integrate with ASP.NET Core rate limiting to protect your API:

```csharp
// Configure rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("DataSurfacePolicy", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 10;
    });
});

// Enable rate limiting on DataSurface endpoints
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableRateLimiting = true,
    RateLimitingPolicy = "DataSurfacePolicy"
});

// Don't forget to use the rate limiter middleware
app.UseRateLimiter();
```

**Per-Resource Policies:**

Configure different policies per resource using `[CrudAuthorize]`:

```csharp
[CrudResource("high-traffic")]
[CrudAuthorize(RateLimitingPolicy = "HighTrafficPolicy")]
public class HighTrafficResource { /* ... */ }
```

---

## API Key Authentication

Enable API key authentication for machine-to-machine access:

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableApiKeyAuth = true,
    ApiKeyHeaderName = "X-Api-Key"  // Default header name
});
```

**Request:**
```http
GET /api/users
X-Api-Key: your-api-key-here
```

**Custom Validation:**

Implement `IApiKeyValidator` for custom validation logic:

```csharp
using DataSurface.Http;

public class DatabaseApiKeyValidator : IApiKeyValidator
{
    private readonly AppDbContext _db;
    
    public DatabaseApiKeyValidator(AppDbContext db) => _db = db;
    
    public async Task<bool> ValidateAsync(string apiKey, CancellationToken ct)
    {
        return await _db.ApiKeys
            .AnyAsync(k => k.Key == apiKey && k.IsActive && k.ExpiresAt > DateTime.UtcNow, ct);
    }
}

// Register
builder.Services.AddScoped<IApiKeyValidator, DatabaseApiKeyValidator>();
```

**Default Behavior:**
- Without `IApiKeyValidator`, any non-empty API key is accepted
- With `IApiKeyValidator`, the validator determines validity
- Missing or invalid API keys return HTTP 401 Unauthorized

---

## Audit Logging

Track all CRUD operations using `IAuditLogger`:

```csharp
using DataSurface.EFCore.Interfaces;

public class DatabaseAuditLogger : IAuditLogger
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    
    public DatabaseAuditLogger(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }
    
    public async Task LogAsync(AuditLogEntry entry, CancellationToken ct)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _http.HttpContext?.User.FindFirst("sub")?.Value,
            Operation = entry.Operation.ToString(),
            ResourceKey = entry.ResourceKey,
            EntityId = entry.EntityId,
            Timestamp = entry.Timestamp,
            Success = entry.Success,
            Changes = entry.Changes?.ToJsonString(),
            PreviousValues = entry.PreviousValues?.ToJsonString()
        });
        await _db.SaveChangesAsync(ct);
    }
}

// Register
builder.Services.AddScoped<IAuditLogger, DatabaseAuditLogger>();
```

**`AuditLogEntry` properties:**
- `Operation` — The CRUD operation performed
- `ResourceKey` — The resource being accessed
- `EntityId` — The entity ID (if applicable)
- `Timestamp` — UTC timestamp
- `Success` — Whether the operation succeeded
- `Changes` — JSON of fields written (create/update)
- `PreviousValues` — JSON of previous values (update)

---

## Structured Logging

Both `EfDataSurfaceCrudService` and `DynamicDataSurfaceCrudService` emit structured logs:

```
[DBG] List User page=1 pageSize=20
[DBG] List User completed in 45ms, returned 20/142 items
[INF] Created User in 12ms
[INF] Updated User id=5 in 8ms
[INF] Deleted User id=5 in 3ms
```

**Log levels:**
- `Debug` — Operation start and read completions
- `Information` — Mutating operations (create, update, delete)

**Structured properties:**
- `{Resource}` — Resource key
- `{Id}` — Entity ID (when applicable)
- `{ElapsedMs}` — Operation duration
- `{Count}` / `{Total}` — List result counts

---

## Metrics

OpenTelemetry-compatible metrics via `DataSurfaceMetrics`:

```csharp
// Register metrics
builder.Services.AddSingleton<DataSurfaceMetrics>();

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("DataSurface"));
```

**Available metrics:**
| Metric | Type | Description |
|--------|------|-------------|
| `datasurface.operations` | Counter | Total CRUD operations by resource and operation |
| `datasurface.errors` | Counter | Failed operations by resource, operation, and error type |
| `datasurface.operation.duration` | Histogram | Operation duration in milliseconds |
| `datasurface.rows_affected` | Counter | Rows affected by operations |

---

## Distributed Tracing

Activity/span integration via `DataSurfaceTracing`:

```csharp
// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("DataSurface"));
```

**Trace attributes:**
- `datasurface.resource` — Resource key
- `datasurface.operation` — CRUD operation
- `datasurface.entity_id` — Entity ID (when applicable)
- `datasurface.rows_affected` — Rows returned/affected
- `datasurface.query.*` — Query parameters (page, page_size, filter_count, sort_count)

---

## Health Checks

Built-in `IHealthCheck` implementations:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<DataSurfaceDbHealthCheck>("datasurface-db")
    .AddCheck<DataSurfaceContractsHealthCheck>("datasurface-contracts")
    .AddCheck<DynamicMetadataHealthCheck>("datasurface-dynamic-metadata")
    .AddCheck<DynamicContractsHealthCheck>("datasurface-dynamic-contracts");
```

**Health checks:**
- `DataSurfaceDbHealthCheck` — Database connectivity
- `DataSurfaceContractsHealthCheck` — Static contracts loaded
- `DynamicMetadataHealthCheck` — Dynamic entity definitions table accessible
- `DynamicContractsHealthCheck` — Dynamic contracts loaded

---

## Schema Endpoint

Get JSON Schema for any resource:

```http
GET /api/$schema/users
```

**Response:**
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

Useful for:
- Client-side form generation
- API documentation
- Contract validation

---

## Attributes Reference

### `[CrudResource]`

Marks a class as a CRUD resource.

```csharp
[CrudResource("users", 
    ResourceKey = "User",           // Default: class name
    MaxPageSize = 200,              // Default: 200
    MaxExpandDepth = 2,             // Default: 1
    EnableList = true,              // Default: true
    EnableGet = true,               // Default: true
    EnableCreate = true,            // Default: true
    EnableUpdate = true,            // Default: true
    EnableDelete = true)]           // Default: true
public class User { }
```

### `[CrudKey]`

Marks the primary key property.

```csharp
[CrudKey(ApiName = "id")]  // Optional: customize API name
public int Id { get; set; }
```

### `[CrudField]`

Controls field visibility and behavior.

```csharp
[CrudField(
    CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort,
    ApiName = "email",              // Optional: customize API name
    RequiredOnCreate = true,        // Validation: required on POST
    Immutable = false,              // If true: rejected on PATCH
    Hidden = false,                 // If true: never exposed
    MinLength = 1,                  // String validation
    MaxLength = 255,                // String validation
    Min = 0,                        // Numeric validation
    Max = 100,                      // Numeric validation
    Regex = @"^[\w@.]+$")]          // Pattern validation
public string Email { get; set; }
```

**`CrudDto` flags:**

| Flag | Effect |
|------|--------|
| `Read` | Included in GET responses |
| `Create` | Accepted in POST body |
| `Update` | Accepted in PATCH body |
| `Filter` | Can be used in `filter[field]=value` |
| `Sort` | Can be used in `sort=field` |

### `[CrudRelation]`

Configures navigation property behavior.

```csharp
[CrudRelation(
    ReadExpandAllowed = true,       // Can use expand=author
    DefaultExpanded = false,        // Auto-expand without asking
    WriteMode = RelationWriteMode.ById,  // How to write
    WriteFieldName = "authorId",    // Field name for writes
    RequiredOnCreate = false)]
public User Author { get; set; }
```

**`RelationWriteMode` options:**
- `None` — Cannot write relation
- `ById` — Write via FK field (e.g., `authorId`)
- `ByIdList` — Write via ID array (e.g., `tagIds`)
- `NestedDisabled` — Nested objects rejected

### `[CrudConcurrency]`

Marks a row version field for optimistic concurrency.

```csharp
[CrudConcurrency(RequiredOnUpdate = true)]
public byte[] RowVersion { get; set; }
```

### `[CrudAuthorize]`

Sets authorization policies per operation.

```csharp
[CrudAuthorize(Policy = "AdminOnly")]  // All operations
[CrudAuthorize(Operation = CrudOperation.Delete, Policy = "SuperAdmin")]
public class User { }
```

### `[CrudHidden]`

Completely hides a property from the contract.

```csharp
[CrudHidden]
public string InternalSecret { get; set; }
```

### `[CrudIgnore]`

Excludes a property from contract generation (use for EF navigation properties you don't want exposed).

## Configuration Options

### `DataSurfaceEfCoreOptions`

```csharp
builder.Services.AddDataSurfaceEfCore(opt =>
{
    opt.AssembliesToScan = [typeof(Program).Assembly];
    opt.AutoRegisterCrudEntities = true;    // Auto-register in DbContext
    opt.EnableSoftDeleteFilter = true;      // Apply IsDeleted filter
    opt.EnableRowVersionConvention = true;  // Configure RowVersion columns
    opt.EnableTimestampConvention = true;   // Auto-populate CreatedAt/UpdatedAt
    opt.UseCamelCaseApiNames = true;        // camelCase API names
    
    opt.ContractBuilderOptions.ExposeFieldsOnlyWhenAnnotated = true;
});
```

### `DataSurfaceHttpOptions`

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    ApiPrefix = "/api",                     // Route prefix
    MapStaticResources = true,              // Map static entity routes
    MapDynamicCatchAll = true,              // Map /api/d/{route}
    DynamicPrefix = "/d",                   // Dynamic route prefix
    MapResourceDiscoveryEndpoint = true,    // GET /api/$resources
    RequireAuthorizationByDefault = false,  // Require auth on all endpoints
    DefaultPolicy = null,                   // Default auth policy
    EnableEtags = true,                     // ETag response headers
    ThrowOnRouteCollision = false,          // Fail on duplicate routes
    EnablePutForFullUpdate = false,         // Enable PUT for full replacement
    EnableImportExport = false,             // Enable import/export endpoints
    EnableRateLimiting = false,             // Enable rate limiting
    RateLimitingPolicy = null,              // Rate limiting policy name
    EnableApiKeyAuth = false,               // Enable API key authentication
    ApiKeyHeaderName = "X-Api-Key",         // API key header name
    EnableWebhooks = false                  // Enable webhook publishing
});
```

### `DataSurfaceDynamicOptions`

```csharp
builder.Services.AddDataSurfaceDynamic(opt =>
{
    opt.Schema = "dbo";                     // DB schema for dynamic tables
    opt.WarmUpContractsOnStart = true;      // Load contracts at startup
});
```

### `DataSurfaceAdminOptions`

```csharp
app.MapDataSurfaceAdmin(new DataSurfaceAdminOptions
{
    Prefix = "/admin/ds",                   // Route prefix
    RequireAuthorization = true,            // Require auth
    Policy = "DataSurfaceAdmin"             // Auth policy name
});
```

### Feature Flags

Selectively enable or disable DataSurface features using `DataSurfaceFeatures`. This allows you to use only the features you need, reducing complexity and overhead:

```csharp
builder.Services.AddDataSurfaceEfCore(opt =>
{
    // Use a preset
    opt.Features = DataSurfaceFeatures.Minimal;   // Core CRUD only
    opt.Features = DataSurfaceFeatures.Standard;  // Default - security & observability
    opt.Features = DataSurfaceFeatures.Full;      // All features including webhooks
    
    // Or customize individual features
    opt.Features = new DataSurfaceFeatures
    {
        EnableFieldValidation = true,
        EnableDefaultValues = true,
        EnableComputedFields = true,
        EnableFieldProjection = true,
        EnableTenantIsolation = true,
        EnableRowLevelSecurity = true,
        EnableResourceAuthorization = true,
        EnableFieldAuthorization = true,
        EnableAuditLogging = true,
        EnableMetrics = false,          // Disable metrics
        EnableTracing = false,          // Disable tracing
        EnableQueryCaching = true,
        EnableHooks = true,
        EnableOverrides = true,
        EnableWebhooks = false          // Disable webhooks
    };
});
```

**Available Feature Flags:**

| Category | Feature | Default | Description |
|----------|---------|---------|-------------|
| **Core CRUD** | `EnableFieldValidation` | ✅ | MinLength, MaxLength, Min, Max, Regex, AllowedValues |
| | `EnableDefaultValues` | ✅ | Apply default values on create |
| | `EnableComputedFields` | ✅ | Evaluate computed expressions at read time |
| | `EnableFieldProjection` | ✅ | Support `?fields=` query parameter |
| **Security** | `EnableTenantIsolation` | ✅ | `[CrudTenant]` attribute support |
| | `EnableRowLevelSecurity` | ✅ | `IResourceFilter<T>` support |
| | `EnableResourceAuthorization` | ✅ | `IResourceAuthorizer<T>` support |
| | `EnableFieldAuthorization` | ✅ | `IFieldAuthorizer` support |
| **Observability** | `EnableAuditLogging` | ✅ | `IAuditLogger` integration |
| | `EnableMetrics` | ✅ | OpenTelemetry metrics |
| | `EnableTracing` | ✅ | Distributed tracing |
| **Caching** | `EnableQueryCaching` | ✅ | `IQueryResultCache` integration |
| **Extensibility** | `EnableHooks` | ✅ | Lifecycle hooks |
| | `EnableOverrides` | ✅ | CRUD operation overrides |
| **Integration** | `EnableWebhooks` | ❌ | Webhook publishing (opt-in) |

**Presets:**

| Preset | Description | Use Case |
|--------|-------------|----------|
| `Minimal` | Core CRUD + validation only | Simple APIs, microservices, maximum performance |
| `Standard` | Core + security + observability | Most production applications (default) |
| `Full` | All features enabled | Feature-rich applications with webhooks |

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        HTTP Layer                                │
│  DataSurface.Http: Minimal API mapping, query parsing, ETags    │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                    IDataSurfaceCrudService                       │
│  ListAsync, GetAsync, CreateAsync, UpdateAsync, DeleteAsync     │
└─────────────────────────────────────────────────────────────────┘
                    │                       │
                    ▼                       ▼
┌──────────────────────────────┐ ┌──────────────────────────────┐
│   EfDataSurfaceCrudService   │ │ DynamicDataSurfaceCrudService │
│   DataSurface.EFCore         │ │   DataSurface.Dynamic         │
│   (Static EF entities)       │ │   (JSON records)              │
└──────────────────────────────┘ └──────────────────────────────┘
                    │                       │
                    ▼                       ▼
┌─────────────────────────────────────────────────────────────────┐
│                    ResourceContract                              │
│  DataSurface.Core: Single source of truth                       │
│  - Fields, Relations, Operations, Query limits, Security        │
└─────────────────────────────────────────────────────────────────┘
                    │                       │
                    ▼                       ▼
┌──────────────────────────────┐ ┌──────────────────────────────┐
│     ContractBuilder          │ │   DynamicContractBuilder      │
│  (C# attributes → Contract)  │ │  (DB metadata → Contract)     │
└──────────────────────────────┘ └──────────────────────────────┘
```

### Key abstractions

| Interface | Purpose |
|-----------|---------|
| `IDataSurfaceCrudService` | Executes CRUD operations |
| `IResourceContractProvider` | Provides contracts by resource key |
| `ICrudHook` | Global lifecycle hooks |
| `ICrudHook<T>` | Entity-specific lifecycle hooks |
| `ITimestamped` | Auto-timestamp convention interface |
| `ISoftDelete` | Soft-delete convention interface |
| `IResourceFilter<T>` | Row-level security filtering |
| `IResourceAuthorizer<T>` | Resource instance authorization |
| `IFieldAuthorizer` | Field-level read/write authorization |
| `IAuditLogger` | CRUD operation audit logging |

---

## Quick Checklist

### Required Setup

- [ ] Add package references (`DataSurface.Core`, `DataSurface.EFCore`, `DataSurface.Http`)
- [ ] Annotate entities with `[CrudResource]`, `[CrudKey]`, `[CrudField]`
- [ ] Call `AddDataSurfaceEfCore()` with assemblies to scan
- [ ] Register `CrudHookDispatcher`, `CrudOverrideRegistry`, `EfDataSurfaceCrudService`
- [ ] Register `IDataSurfaceCrudService`
- [ ] Call `app.MapDataSurfaceCrud()`

### Optional Features

- [ ] Add `DataSurface.OpenApi` for Swagger schemas
- [ ] Add `DataSurface.Admin` for runtime entity management
- [ ] Configure `DataSurfaceFeatures` for selective feature enablement
- [ ] Register `IWebhookPublisher` for webhook integration
- [ ] Register `IAuditLogger` for audit logging
- [ ] Register `IApiKeyValidator` for custom API key validation
- [ ] Register `ITenantResolver` for custom tenant resolution
- [ ] Register `IResourceFilter<T>` for row-level security
- [ ] Register `IResourceAuthorizer<T>` for resource authorization
- [ ] Register `IFieldAuthorizer` for field-level authorization

---

## Planned Features

The following features are planned for future releases. Contributions are welcome!

### High Priority

| Feature | Description | Status |
|---------|-------------|--------|
| **GraphQL Endpoint** | `/api/graphql` with auto-generated schema from contracts | Planned |
| **Change Data Capture** | Track historical changes with entity versioning and temporal queries | Planned |
| **Fluent Configuration** | `builder.Resource<T>().Field(x => x.Name).Validation(...)` syntax | Planned |

### Medium Priority

| Feature | Description | Status |
|---------|-------------|--------|
| **Cross-backend Expansion** | Expand dynamic entities referencing EF entities and vice versa | Planned |
| **Async Job Queue** | Background processing for long-running operations with status tracking | Planned |
| **gRPC Support** | gRPC endpoints alongside REST for high-performance scenarios | Planned |
| **Real-time Updates** | SignalR/WebSocket integration for live data subscriptions | Planned |
| **Batch Validation** | Validate multiple entities in a single request before commit | Planned |

### Lower Priority

| Feature | Description | Status |
|---------|-------------|--------|
| **OData Support** | OData query syntax compatibility (`$filter`, `$select`, `$expand`) | Considering |
| **JSON Patch** | RFC 6902 JSON Patch support for partial updates | Considering |
| **Conditional Creates** | `If-None-Match: *` header support for idempotent creates | Considering |
| **Field Masking** | Automatic PII/sensitive data masking in responses | Considering |
| **Query Cost Analysis** | Estimate and limit query complexity before execution | Considering |
| **Multi-database Support** | Route different resources to different databases | Considering |
| **Optimistic Offline Sync** | Conflict resolution for mobile/offline scenarios | Considering |
| **Schema Migrations** | Track and apply contract changes across environments | Considering |

### Community Suggestions

Have a feature request? Open an issue on GitHub with the `enhancement` label!
