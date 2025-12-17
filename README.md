# DataSurface

> **Contract-driven CRUD HTTP endpoints for ASP.NET Core**

DataSurface eliminates CRUD boilerplate by generating fully-featured HTTP endpoints from a single source of truth: the **ResourceContract**. Define your resources once using C# attributes or database metadata, and get automatic validation, filtering, sorting, pagination, and more.

---

## Table of Contents

- [Features](#features)
- [Packages](#packages)
- [Quick Start](#quick-start)
- [Guides](#guides)
  - [Static Resources (EF Core)](#guide-static-resources-ef-core)
  - [Dynamic Resources (Runtime Metadata)](#guide-dynamic-resources-runtime-metadata)
  - [Admin Endpoints](#guide-admin-endpoints)
  - [OpenAPI / Swagger](#guide-openapi--swagger)
- [Attributes Reference](#attributes-reference)
- [Query API](#query-api)
- [Hooks & Overrides](#hooks--overrides)
- [Configuration Options](#configuration-options)
- [Timestamps Convention](#timestamps-convention)
- [Schema Endpoint](#schema-endpoint)
- [Observability](#observability)
- [Performance](#performance)
- [Security](#security)
- [Architecture](#architecture)

---

## Features

| Feature | Description |
|---------|-------------|
| **Auto-generated endpoints** | `GET`, `POST`, `PATCH`, `DELETE` via Minimal APIs |
| **Field-level control** | Choose which fields appear in read/create/update DTOs |
| **Validation** | Required fields, immutable fields, unknown field rejection |
| **Filtering & Sorting** | Allowlisted fields with operators (`eq`, `gt`, `contains`, etc.) |
| **Pagination** | Built-in `page` + `pageSize` with configurable max |
| **Expansion** | `expand=relation` with depth limits |
| **Concurrency** | Row version + `ETag` / `If-Match` headers |
| **Authorization** | Per-operation policy names |
| **Row-level security** | `IResourceFilter<T>` for tenant/user-based query filtering |
| **Resource authorization** | `IResourceAuthorizer<T>` for instance-level access control |
| **Field authorization** | `IFieldAuthorizer` for field-level read/write control |
| **Audit logging** | `IAuditLogger` for tracking all CRUD operations |
| **Hooks** | Global and entity-specific lifecycle hooks |
| **Overrides** | Replace any CRUD operation with custom logic |
| **Soft delete** | Built-in `ISoftDelete` convention support |
| **Timestamps** | Auto-populate `CreatedAt`/`UpdatedAt` via `ITimestamped` |
| **Structured logging** | Built-in `ILogger` integration with operation timing |
| **Metrics** | OpenTelemetry-compatible counters and histograms |
| **Distributed tracing** | Activity/span integration for request tracing |
| **Health checks** | `IHealthCheck` implementations for monitoring |
| **Response caching** | ETag-based 304 responses, configurable Cache-Control |
| **Query caching** | Optional `IDistributedCache` integration |
| **Bulk operations** | Batch create/update/delete via `/bulk` endpoint |
| **Async streaming** | `IAsyncEnumerable` support via `/stream` endpoint |
| **Compiled queries** | Pre-compiled EF Core queries for common operations |
| **Schema endpoint** | `GET /api/$schema/{resource}` returns JSON Schema |
| **HEAD support** | `HEAD` requests return count headers without body |
| **Dynamic entities** | Runtime-defined resources without recompilation |

---

## Packages

| Package | Purpose |
|---------|---------|
| `DataSurface.Core` | Contracts, attributes, and builders |
| `DataSurface.EFCore` | EF Core CRUD service, hooks, query engine |
| `DataSurface.Dynamic` | Runtime metadata storage, dynamic CRUD service |
| `DataSurface.Http` | Minimal API endpoint mapping, query parsing, ETags |
| `DataSurface.Admin` | Admin endpoints for managing dynamic entities |
| `DataSurface.OpenApi` | Swashbuckle integration for typed schemas |
| `DataSurface.Generator` | *(Optional)* Source generator for typed DTOs |

**Typical combinations:**
- **Static only:** `Core` + `EFCore` + `Http`
- **Dynamic only:** `Core` + `Dynamic` + `Http` + `Admin`
- **Both:** All of the above

---

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

---

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

---

## Query API

### List endpoint: `GET /api/{resource}`

| Parameter | Example | Description |
|-----------|---------|-------------|
| `page` | `?page=2` | Page number (1-based, default: 1) |
| `pageSize` | `?pageSize=50` | Items per page (default: 20) |
| `sort` | `?sort=title,-createdAt` | Comma-separated, `-` prefix for descending |
| `filter[field]` | `?filter[status]=active` | Filter by field value |
| `expand` | `?expand=author,tags` | Include related resources |

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
```

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

### HEAD requests

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

### Concurrency (ETag)

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

---

## Hooks & Overrides

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

### Operation Overrides

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
    ThrowOnRouteCollision = false           // Fail on duplicate routes
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

---

## Timestamps Convention

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

## Observability

DataSurface provides comprehensive observability features including structured logging, metrics, tracing, and health checks.

### Structured Logging

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

### Metrics

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

### Distributed Tracing

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

### Health Checks

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

## Performance

DataSurface provides several performance optimizations for high-throughput scenarios.

### Response Caching

Enable ETag-based conditional GET (304 Not Modified) and Cache-Control headers:

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableConditionalGet = true,      // If-None-Match → 304 response
    CacheControlMaxAgeSeconds = 300   // Cache-Control: max-age=300
});
```

Clients can cache responses and send `If-None-Match` headers to receive 304 responses when data hasn't changed.

### Query Result Caching

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

### Bulk Operations

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

### Async Streaming

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

### Compiled Queries

Pre-compiled EF Core queries for common operations:

```csharp
builder.Services.AddSingleton<CompiledQueryCache>();

// Usage in custom code
var cache = sp.GetRequiredService<CompiledQueryCache>();
var findById = cache.GetOrCreateFindByIdQuery<User, int>("Id");
var user = findById(dbContext, 5);
```

---

## Security

DataSurface provides extensible security features beyond endpoint-level authorization.

### Row-Level Security

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

### Field-Level Authorization

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

### Resource-Level Authorization

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

### Audit Logging

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

### Enabling Security Features

Register the security dispatcher to enable all security features:

```csharp
// Register security dispatcher
builder.Services.AddScoped<CrudSecurityDispatcher>();

// Register your security implementations
builder.Services.AddScoped<IResourceFilter<Order>, TenantResourceFilter>();
builder.Services.AddScoped<IFieldAuthorizer, SensitiveFieldAuthorizer>();
builder.Services.AddScoped<IAuditLogger, DatabaseAuditLogger>();
```

---

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

- [ ] Add package references (`DataSurface.Core`, `DataSurface.EFCore`, `DataSurface.Http`)
- [ ] Annotate entities with `[CrudResource]`, `[CrudKey]`, `[CrudField]`
- [ ] Call `AddDataSurfaceEfCore()` with assemblies to scan
- [ ] Register `CrudHookDispatcher`, `CrudOverrideRegistry`, `EfDataSurfaceCrudService`
- [ ] Register `IDataSurfaceCrudService`
- [ ] Call `app.MapDataSurfaceCrud()`
- [ ] *(Optional)* Add `DataSurface.OpenApi` for Swagger schemas
- [ ] *(Optional)* Add `DataSurface.Admin` for runtime entity management
