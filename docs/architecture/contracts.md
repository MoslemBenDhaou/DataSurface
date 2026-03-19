# Contract System

The **ResourceContract** is the single source of truth for every CRUD resource in DataSurface. All runtime features — endpoints, validation, filtering, sorting, expansion, authorization, hooks, and overrides — consume this contract.

---

## How Contracts Are Produced

### From C# Attributes (Static Resources)

The `ContractBuilder` scans assemblies for classes annotated with `[CrudResource]` and builds a `ResourceContract` for each one:

```csharp
[CrudResource("users", MaxPageSize = 100)]
public class User
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, RequiredOnCreate = true)]
    public string Email { get; set; } = default!;
}
```

For the full list of attributes and how they map to contract properties, see [Attributes Reference](../reference/attributes.md).

### From Database Metadata (Dynamic Resources)

The `DynamicContractBuilder` reads `EntityDef` and `PropertyDef` rows from the database and produces the same `ResourceContract` structure. See [Dynamic Entities](../features/dynamic-entities.md).

---

## Contract Schema

### ResourceContract

The root object describing a CRUD resource.

```csharp
public sealed record ResourceContract(
    string ResourceKey,                                    // Stable identifier (e.g., "User")
    string Route,                                          // URL segment (e.g., "users")
    StorageBackend Backend,                                // Storage backend type
    ResourceKeyContract Key,                               // Primary key definition
    QueryContract Query,                                   // Filtering, sorting, pagination limits
    ReadContract Read,                                     // Expansion rules
    IReadOnlyList<FieldContract> Fields,                   // All scalar fields
    IReadOnlyList<RelationContract> Relations,             // All navigation properties
    IReadOnlyDictionary<CrudOperation, OperationContract> Operations,  // Per-operation config
    SecurityContract Security                              // Authorization policies
);
```

### ResourceKeyContract

```csharp
public sealed record ResourceKeyContract(
    string Name,      // CLR property name (e.g., "Id")
    FieldType Type    // Key type: Int32, Int64, Guid, or String
);
```

### QueryContract

Defines what queries are allowed against this resource.

```csharp
public sealed record QueryContract(
    int MaxPageSize,                           // Maximum items per page (default: 200)
    IReadOnlyList<string> FilterableFields,   // Fields allowed in filter[field]=value
    IReadOnlyList<string> SortableFields,     // Fields allowed in sort=field
    IReadOnlyList<string> SearchableFields,   // Fields included in full-text search
    string? DefaultSort                        // Optional default sort (e.g., "-createdAt")
);
```

### ReadContract

Controls expansion and projection at read time.

```csharp
public sealed record ReadContract(
    IReadOnlyList<string> ExpandAllowed,   // Relations that may be expanded
    int MaxExpandDepth,                     // Maximum expansion depth (default: 1)
    IReadOnlyList<string> DefaultExpand    // Relations expanded by default
);
```

### OperationContract

Per-operation configuration including input/output shapes.

```csharp
public sealed record OperationContract(
    bool Enabled,                              // Whether this operation is available
    IReadOnlyList<string> InputShape,          // Fields accepted in request body (API names)
    IReadOnlyList<string> OutputShape,         // Fields returned in response (API names)
    IReadOnlyList<string> RequiredOnCreate,    // Fields required on POST
    IReadOnlyList<string> ImmutableFields,     // Fields that cannot be changed on PATCH
    ConcurrencyContract? Concurrency           // Optional concurrency settings
);
```

### FieldContract

Describes a scalar field exposed by a resource.

```csharp
public sealed record FieldContract(
    string Name,                       // CLR property name
    string ApiName,                    // External API name (typically camelCase)
    FieldType Type,                    // Data type
    bool Nullable,                     // Whether null is allowed
    bool InRead,                       // Included in GET responses
    bool InCreate,                     // Accepted in POST body
    bool InUpdate,                     // Accepted in PATCH body
    bool Filterable,                   // Can use filter[field]=value
    bool Sortable,                     // Can use sort=field
    bool Hidden,                       // Hard-hidden (never exposed)
    bool Immutable,                    // Cannot be changed after creation
    bool Searchable,                   // Included in full-text search
    bool Computed,                     // Server-calculated read-only field
    string? ComputedExpression,        // Expression for computed fields
    object? DefaultValue,              // Default value applied on create
    FieldValidationContract Validation // Validation rules
);
```

### FieldValidationContract

```csharp
public sealed record FieldValidationContract(
    bool RequiredOnCreate,                     // Must be present on POST
    int? MinLength,                            // Minimum string length
    int? MaxLength,                            // Maximum string length
    decimal? Min,                              // Minimum numeric value
    decimal? Max,                              // Maximum numeric value
    string? Regex,                             // Pattern constraint
    IReadOnlyList<string>? AllowedValues       // Enum-like value restriction
);
```

### RelationContract

Describes a navigation property relationship.

```csharp
public sealed record RelationContract(
    string Name,                    // CLR navigation property name
    string ApiName,                 // External API name
    RelationKind Kind,              // Cardinality (ManyToOne, OneToMany, etc.)
    string TargetResourceKey,       // Related resource key
    RelationReadContract Read,      // Expansion behavior
    RelationWriteContract Write     // Write behavior
);
```

### RelationReadContract

```csharp
public sealed record RelationReadContract(
    bool ExpandAllowed,     // Can use expand=relation
    bool DefaultExpanded    // Automatically expanded without asking
);
```

### RelationWriteContract

```csharp
public sealed record RelationWriteContract(
    RelationWriteMode Mode,         // How writes are performed
    string? WriteFieldName,         // API field name for writes (e.g., "userId")
    bool RequiredOnCreate,          // Required on POST
    string? ForeignKeyProperty      // CLR FK property name
);
```

### SecurityContract

```csharp
public sealed record SecurityContract(
    IReadOnlyDictionary<CrudOperation, string?> Policies  // Policy name per operation
);
```

### ConcurrencyContract

```csharp
public sealed record ConcurrencyContract(
    ConcurrencyMode Mode,       // None, RowVersion, or ETag
    string FieldApiName,        // API name of concurrency field
    bool RequiredOnUpdate       // Whether token is required on PATCH
);
```

---

## JSON Representation

Contracts can be serialized as JSON — useful for debugging, dynamic definitions, and the schema endpoint:

```json
{
  "resourceKey": "Post",
  "route": "posts",
  "backend": "EfCore",
  "key": { "name": "Id", "type": "Int32" },
  "query": {
    "maxPageSize": 200,
    "filterableFields": ["id", "title", "authorId"],
    "sortableFields": ["id", "title", "createdAt"],
    "searchableFields": ["title", "content"],
    "defaultSort": "-createdAt"
  },
  "read": {
    "expandAllowed": ["author", "tags"],
    "maxExpandDepth": 1,
    "defaultExpand": []
  },
  "fields": [
    {
      "name": "Id", "apiName": "id", "type": "Int32",
      "inRead": true, "filterable": true, "sortable": true, "immutable": true
    },
    {
      "name": "Title", "apiName": "title", "type": "String",
      "inRead": true, "inCreate": true, "inUpdate": true,
      "filterable": true, "sortable": true,
      "validation": { "requiredOnCreate": true, "maxLength": 200 }
    }
  ],
  "relations": [
    {
      "name": "Author", "apiName": "author",
      "kind": "ManyToOne", "targetResourceKey": "User",
      "read": { "expandAllowed": true, "defaultExpanded": false },
      "write": { "mode": "ById", "writeFieldName": "authorId", "requiredOnCreate": true }
    }
  ],
  "security": {
    "policies": {
      "List": null, "Get": null,
      "Create": "Authenticated", "Update": "Authenticated", "Delete": "Admin"
    }
  }
}
```

---

## Safety Defaults

These defaults are enforced unless explicitly relaxed:

| Rule | Default |
|------|---------|
| **Opt-in exposure** | Only `[CrudResource]` classes become endpoints |
| **Field allowlist** | Only annotated fields are accepted/emitted |
| **Unknown field rejection** | Unknown fields in request bodies → 400 |
| **No nested writes** | Relations written by ID only |
| **Controlled expansion** | Allowlist + depth limit (default: 1) |
| **Required pagination** | All lists are paged (default: 20, max: 200) |
| **Filter/sort allowlists** | Only explicitly allowed fields |
| **Startup validation** | Invalid contracts fail fast with diagnostics |

---

## Next

- [Request Lifecycle](request-lifecycle.md) — How the contract drives a request end-to-end
- [Enums & Types Reference](../reference/enums.md) — All enum values used in contracts
