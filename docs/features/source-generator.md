# Source Generator

DataSurface includes an optional Roslyn source generator (`DataSurface.Generator`) that produces typed DTOs and endpoint mapping helpers at compile time. This eliminates the need for reflection-based DTO construction at runtime.

---

## Setup

Add the generator package:

```xml
<PackageReference Include="DataSurface.Generator" Version="*" PrivateAssets="all" />
```

The generator targets `netstandard2.0` and is packaged as a Roslyn analyzer (`analyzers/dotnet/cs`), so it loads in every compiler host — IDE, `dotnet build`, and MSBuild.exe. It runs automatically during compilation — no additional configuration is needed.

---

## What It Generates

For each class **or record** annotated with `[CrudResource]`, the generator produces:

- **Read DTO** — Contains only fields with `CrudDto.Read`
- **Create DTO** — Contains only fields with `CrudDto.Create` (`[Required]` is applied to `RequiredOnCreate` fields)
- **Update DTO** — Contains only fields with `CrudDto.Update`; all members are nullable/optional
- **Endpoint mapping helpers** — A `MapDataSurfaceGeneratedCrud()` extension that maps minimal-API routes, emitted only when the project references `DataSurface.EFCore`

Every generated DTO property carries `[JsonPropertyName]` with the field's API name, so the generated endpoints round-trip camelCase JSON correctly with default serializer options.

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

The generator produces DTOs equivalent to:

```csharp
public sealed class UserReadDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("email")] public string Email { get; set; } = default!;
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
}

public sealed class UserCreateDto
{
    [JsonPropertyName("email")] [Required] public string Email { get; set; } = default!;
}

public sealed class UserUpdateDto
{
    [JsonPropertyName("email")] public string? Email { get; set; }
}
```

---

## How It Works

The `CrudGenerator` is a true incremental source generator built on `ForAttributeWithMetadataName` with fully value-equatable models (no `ISymbol` or `Compilation` captured), so IDE incremental caching actually works and there is no compiler-host memory leak. It:

1. Matches classes and records with the `[CrudResource]` attribute (generic entity types are diagnosed and skipped)
2. Discovers the key the same way `ContractBuilder` does: `CrudResourceAttribute.KeyProperty`, then `[CrudKey]`, then an `Id` or `{TypeName}Id` property — inherited properties included
3. Extracts field definitions from `[CrudField]`, honoring `[CrudIgnore]` and `[CrudHidden]`; tenant fields are never client-writable
4. Honors the `EnableList` / `EnableGet` / `EnableCreate` / `EnableUpdate` / `EnableDelete` flags (disabled operations get no endpoints) and applies per-operation `[CrudAuthorize]` policies via `RequireAuthorization`
5. Emits DTOs and (when `DataSurface.EFCore` is referenced) the endpoint mapper, using fully-qualified `global::` type names and escaped string literals

### Diagnostics

The generator reports compile-time errors:

| ID | Description |
|----|-------------|
| `DSG001` | The `[CrudResource]` route is missing or empty |
| `DSG002` | Multiple fields resolve to the same API name |
| `DSG003` | No key property found — add `[CrudKey]`, an `Id`/`{TypeName}Id` property, or set `KeyProperty` |
| `DSG004` | Multiple `[CrudKey]` properties found; only one is allowed |
| `DSG005` | A property has both `[CrudIgnore]` and `[CrudField]` |
| `DSG006` | Generic entity types are not supported |
| `DSG007` | An `ApiName` does not produce a valid C# identifier |
| `DSG008` | `KeyProperty` names a property that does not exist on the resource type |
| `DSG009` | `[CrudField]` applied to a navigation-shaped (non-scalar) property — use `[CrudRelation]` instead |

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
