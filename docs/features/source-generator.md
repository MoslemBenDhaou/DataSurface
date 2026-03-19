# Source Generator

DataSurface includes an optional Roslyn source generator (`DataSurface.Generator`) that produces typed DTOs and endpoint mapping helpers at compile time. This eliminates the need for reflection-based DTO construction at runtime.

---

## Setup

Add the generator package:

```xml
<PackageReference Include="DataSurface.Generator" Version="*" />
```

The generator automatically runs during compilation — no additional configuration is needed.

---

## What It Generates

For each class annotated with `[CrudResource]`, the generator produces:

- **Read DTO** — Contains only fields with `CrudDto.Read`
- **Create DTO** — Contains only fields with `CrudDto.Create`
- **Update DTO** — Contains only fields with `CrudDto.Update`
- **Endpoint mapping helpers** — Typed route handlers

### Example

Given this entity:

```csharp
[CrudResource("users")]
public class User
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, RequiredOnCreate = true)]
    public string Email { get; set; } = default!;

    [CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
    public DateTime CreatedAt { get; set; }
}
```

The generator produces typed DTOs equivalent to:

```csharp
public record UserReadDto(int Id, string Email, DateTime CreatedAt);
public record UserCreateDto(string Email);
public record UserUpdateDto(string? Email);
```

---

## How It Works

The `CrudGenerator` is a Roslyn incremental source generator that:

1. Identifies classes with `[CrudResource]` attribute
2. Extracts field definitions from `[CrudKey]`, `[CrudField]`, and `[CrudRelation]` attributes
3. Generates strongly-typed DTO records and mapping code
4. Emits diagnostics for misconfigured attributes

### Diagnostics

The generator reports compile-time warnings and errors:

- Missing `[CrudKey]` on a `[CrudResource]` class
- Invalid `CrudDto` flag combinations
- Unsupported property types

---

## When to Use

The source generator is **optional**. DataSurface works fully without it — the runtime uses reflection-based JSON mapping by default.

Use the generator when:
- You want compile-time type safety for DTOs
- You want to eliminate reflection overhead
- You need generated types for downstream code (e.g., client SDK generation)

---

## Related

- [CRUD Operations](crud-operations.md) — How DTOs are used in the request/response cycle
- [Attributes Reference](../reference/attributes.md) — Attributes consumed by the generator
