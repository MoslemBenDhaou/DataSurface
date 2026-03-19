# Quick Start

Build a working DataSurface API in three steps.

## Step 1: Define Your Entity

Annotate a C# class with DataSurface attributes to describe what should be exposed via the API:

```csharp
using DataSurface.Core.Annotations;
using DataSurface.Core.Enums;

[CrudResource("users")]
public class User
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update,
        RequiredOnCreate = true, MaxLength = 255)]
    public string Email { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update,
        RequiredOnCreate = true)]
    public string Name { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public DateTime CreatedAt { get; set; }

    [CrudConcurrency]
    public byte[] RowVersion { get; set; } = default!;
}
```

Key points:
- `[CrudResource("users")]` — registers the class as a CRUD resource at `/api/users`
- `[CrudKey]` — marks the primary key
- `[CrudField(...)]` — controls which DTOs include this field and its validation rules
- `[CrudConcurrency]` — enables optimistic concurrency via ETags

## Step 2: Register Services

```csharp
using DataSurface.EFCore.Services;
using DataSurface.EFCore.Context;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// EF Core
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// DataSurface contracts and EF Core services
builder.Services.AddDataSurfaceEfCore(opt =>
{
    opt.AssembliesToScan = [typeof(Program).Assembly];
});

// DataSurface CRUD runtime
builder.Services.AddScoped<CrudHookDispatcher>();
builder.Services.AddSingleton<CrudOverrideRegistry>();
builder.Services.AddScoped<EfDataSurfaceCrudService>();
builder.Services.AddScoped<IDataSurfaceCrudService>(sp =>
    sp.GetRequiredService<EfDataSurfaceCrudService>());
```

Your `DbContext` should extend `DeclarativeDbContext` to get automatic convention support:

```csharp
public class AppDbContext : DeclarativeDbContext<AppDbContext>
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        DataSurfaceEfCoreOptions dsOptions,
        IResourceContractProvider contracts)
        : base(options, dsOptions, contracts) { }
}
```

## Step 3: Map Endpoints

```csharp
using DataSurface.Http;

var app = builder.Build();

app.MapDataSurfaceCrud();

app.Run();
```

## Result

Your API now has these endpoints:

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/users` | List with filtering, sorting, pagination |
| `HEAD` | `/api/users` | Count only (`X-Total-Count` header) |
| `GET` | `/api/users/{id}` | Get single user |
| `POST` | `/api/users` | Create a new user |
| `PATCH` | `/api/users/{id}` | Partial update |
| `DELETE` | `/api/users/{id}` | Delete user |
| `GET` | `/api/$schema/users` | JSON Schema for resource |
| `GET` | `/api/$resources` | List all available resources |

### Try It

```bash
# Create a user
curl -X POST /api/users \
  -H "Content-Type: application/json" \
  -d '{"email": "alice@example.com", "name": "Alice"}'

# List users with filtering and sorting
curl "/api/users?filter[name]=contains:alice&sort=-createdAt&page=1&pageSize=10"

# Get a single user
curl /api/users/1

# Update a user
curl -X PATCH /api/users/1 \
  -H "Content-Type: application/json" \
  -d '{"name": "Alice Johnson"}'

# Delete a user
curl -X DELETE /api/users/1
```

---

## Adding More Entities

Add as many resources as you need — each one is just a class with attributes:

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

    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public DateTime CreatedAt { get; set; }

    [CrudRelation(ReadExpandAllowed = true, WriteMode = RelationWriteMode.ById)]
    public User Author { get; set; } = default!;

    [CrudConcurrency]
    public byte[] RowVersion { get; set; } = default!;
}
```

All entities discovered by `AssembliesToScan` are automatically registered — no additional wiring needed.

---

## Adding Dynamic Resources

To support runtime-defined entities alongside static ones, see [Dynamic Entities](../features/dynamic-entities.md).

## Adding OpenAPI / Swagger

To generate typed schemas, see [OpenAPI Integration](../features/openapi.md).

## Next Steps

- [Configuration](configuration.md) — Customize behavior with options
- [Architecture Overview](../architecture/overview.md) — Understand how the pieces fit together
- [Features](../features/crud-operations.md) — Explore the full feature set
