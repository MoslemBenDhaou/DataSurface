# Contract Definition

> **Quick Reference Guide** — This document details the internal contract schema that powers DataSurface. For usage examples and getting started, see [README.md](README.md).

DataSurface uses a unified **ResourceContract** as the single source of truth. This contract can be produced from either C# attributes (static entities) or EntityDef/PropertyDef database metadata (dynamic entities). All runtime features—CRUD endpoints, validation, filtering, sorting, expansion, authorization, hooks, and overrides—consume this contract.

---

## Table of Contents

- [1. Core Concepts](#1-core-concepts)
- [2. Contract Schema Reference](#2-contract-schema-reference)
  - [ResourceContract](#resourcecontract)
  - [ResourceKeyContract](#resourcekeycontract)
  - [QueryContract](#querycontract)
  - [ReadContract](#readcontract)
  - [OperationContract](#operationcontract)
  - [FieldContract](#fieldcontract)
  - [FieldValidationContract](#fieldvalidationcontract)
  - [RelationContract](#relationcontract)
  - [RelationReadContract](#relationreadcontract)
  - [RelationWriteContract](#relationwritecontract)
  - [SecurityContract](#securitycontract)
  - [ConcurrencyContract](#concurrencycontract)
- [3. Enums Reference](#3-enums-reference)
- [4. Attribute-to-Contract Mapping](#4-attribute-to-contract-mapping)
- [5. API Surface Specification](#5-api-surface-specification)
- [6. Error Responses](#6-error-responses)
- [7. Safety Defaults](#7-safety-defaults)

---

## 1. Core Concepts

| Term | Description |
|------|-------------|
| **Resource** | A CRUD-exposed entity (e.g., `users`, `posts`, `invoices`) |
| **Operation** | One of: `List`, `Get`, `Create`, `Update`, `Delete` |
| **Contract** | Normalized metadata describing exposure, validation, relationships, query capabilities, and security |
| **Backend** | Storage mechanism: `EfCore`, `DynamicJson`, `DynamicEav`, or `DynamicHybrid` |

---

## 2. Contract Schema Reference

### ResourceContract

The root contract describing a CRUD resource.

```csharp
// DataSurface.Core.Contracts.ResourceContract
public sealed record ResourceContract(
    string ResourceKey,                                    // Stable identifier (e.g., "User", "Post")
    string Route,                                          // URL segment (e.g., "users", "posts")
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

Primary key definition for a resource.

```csharp
// DataSurface.Core.Contracts.ResourceKeyContract
public sealed record ResourceKeyContract(
    string Name,      // CLR property name (e.g., "Id")
    FieldType Type    // Key type: Int32, Int64, Guid, or String
);
```

### QueryContract

Query allowlists and pagination limits.

```csharp
// DataSurface.Core.Contracts.QueryContract
public sealed record QueryContract(
    int MaxPageSize,                           // Maximum page size (default: 200)
    IReadOnlyList<string> FilterableFields,   // Fields allowed in filter[field]=value
    IReadOnlyList<string> SortableFields,     // Fields allowed in sort=field
    string? DefaultSort                        // Optional default sort (e.g., "-createdAt")
);
```

### ReadContract

Read-time expansion and projection rules.

```csharp
// DataSurface.Core.Contracts.ReadContract
public sealed record ReadContract(
    IReadOnlyList<string> ExpandAllowed,   // Relations that may be expanded
    int MaxExpandDepth,                     // Maximum expansion depth (default: 1)
    IReadOnlyList<string> DefaultExpand    // Relations expanded by default
);
```

### OperationContract

Per-operation contract with input/output shapes and validation rules.

```csharp
// DataSurface.Core.Contracts.OperationContract
public sealed record OperationContract(
    bool Enabled,                              // Whether operation is available
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
// DataSurface.Core.Contracts.FieldContract
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
    FieldValidationContract Validation // Validation rules
);
```

### FieldValidationContract

Validation rules for a field.

```csharp
// DataSurface.Core.Contracts.FieldValidationContract
public sealed record FieldValidationContract(
    bool RequiredOnCreate,   // Must be present on POST
    int? MinLength,          // Minimum string length
    int? MaxLength,          // Maximum string length
    decimal? Min,            // Minimum numeric value
    decimal? Max,            // Maximum numeric value
    string? Regex            // Pattern constraint for strings
);
```

### RelationContract

Describes a navigation property relationship.

```csharp
// DataSurface.Core.Contracts.RelationContract
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

Read/expansion behavior for a relation.

```csharp
// DataSurface.Core.Contracts.RelationReadContract
public sealed record RelationReadContract(
    bool ExpandAllowed,     // Can use expand=relation
    bool DefaultExpanded    // Automatically expanded without asking
);
```

### RelationWriteContract

Write behavior for a relation.

```csharp
// DataSurface.Core.Contracts.RelationWriteContract
public sealed record RelationWriteContract(
    RelationWriteMode Mode,         // How writes are performed
    string? WriteFieldName,         // API field name for writes (e.g., "userId")
    bool RequiredOnCreate,          // Required on POST
    string? ForeignKeyProperty      // CLR FK property name
);
```

### SecurityContract

Per-operation authorization policies.

```csharp
// DataSurface.Core.Contracts.SecurityContract
public sealed record SecurityContract(
    IReadOnlyDictionary<CrudOperation, string?> Policies  // Policy name per operation
);
```

### ConcurrencyContract

Optimistic concurrency configuration.

```csharp
// DataSurface.Core.Contracts.ConcurrencyContract
public sealed record ConcurrencyContract(
    ConcurrencyMode Mode,       // None, RowVersion, or ETag
    string FieldApiName,        // API name of concurrency field
    bool RequiredOnUpdate       // Whether token is required on PATCH
);
```

---

## 3. Enums Reference

### CrudDto (Flags)

Field participation in DTO shapes and query capabilities.

| Flag | Value | Description |
|------|-------|-------------|
| `None` | 0 | Not included in any shape |
| `Read` | 1 | Included in GET responses |
| `Create` | 2 | Accepted in POST body |
| `Update` | 4 | Accepted in PATCH body |
| `Filter` | 8 | Can use `filter[field]=value` |
| `Sort` | 16 | Can use `sort=field` |

### CrudOperation

CRUD operations.

| Value | Description |
|-------|-------------|
| `List` | GET collection |
| `Get` | GET single item |
| `Create` | POST new item |
| `Update` | PATCH existing item |
| `Delete` | DELETE item |

### StorageBackend

Backend storage types.

| Value | Description |
|-------|-------------|
| `EfCore` | Entity Framework Core entities |
| `DynamicJson` | JSON-based dynamic storage |
| `DynamicEav` | Entity-Attribute-Value storage |
| `DynamicHybrid` | Hybrid dynamic storage |

### FieldType

Canonical field types.

| Value | C# Type |
|-------|---------|
| `String` | `string` |
| `Int32` | `int` |
| `Int64` | `long` |
| `Decimal` | `decimal` |
| `Boolean` | `bool` |
| `DateTime` | `DateTime` |
| `Guid` | `Guid` |
| `Json` | `JsonNode` / `JsonObject` |
| `Enum` | Enum types |
| `StringArray` | `string[]` |
| `IntArray` | `int[]` |
| `GuidArray` | `Guid[]` |
| `DecimalArray` | `decimal[]` |

### RelationKind

Relationship cardinality.

| Value | Description |
|-------|-------------|
| `ManyToOne` | FK reference (e.g., `Post.Author`) |
| `OneToMany` | Collection (e.g., `User.Posts`) |
| `ManyToMany` | Junction table relationship |
| `OneToOne` | 1:1 relationship |

### RelationWriteMode

How relation writes are performed.

| Value | Description |
|-------|-------------|
| `None` | No write support |
| `ById` | Write via FK field (e.g., `authorId`) |
| `ByIdList` | Write via ID array (e.g., `tagIds`) |
| `NestedDisabled` | Nested objects rejected |

### ConcurrencyMode

Concurrency control mechanism.

| Value | Description |
|-------|-------------|
| `None` | No concurrency control |
| `RowVersion` | `byte[]` row version token |
| `ETag` | HTTP ETag-based token |

---

## 4. Attribute-to-Contract Mapping

### Attribute Reference

| Attribute | Target | Maps To |
|-----------|--------|---------|
| `[CrudResource("route")]` | Class | `ResourceContract` |
| `[CrudKey]` | Property | `ResourceKeyContract` |
| `[CrudField(CrudDto.Read \| ...)]` | Property | `FieldContract` |
| `[CrudRelation(...)]` | Navigation | `RelationContract` |
| `[CrudConcurrency]` | Property | `ConcurrencyContract` |
| `[CrudAuthorize(Policy = "...")]` | Class | `SecurityContract` |
| `[CrudHidden]` | Property | Field excluded from contract |
| `[CrudIgnore]` | Property | Property ignored during contract generation |

### CrudResourceAttribute Properties

```csharp
[CrudResource("users",
    ResourceKey = "User",        // Default: class name
    Backend = StorageBackend.EfCore,
    KeyProperty = "Id",          // Override key discovery
    MaxPageSize = 200,           // Default: 200
    MaxExpandDepth = 1,          // Default: 1
    EnableList = true,           // Default: true
    EnableGet = true,            // Default: true
    EnableCreate = true,         // Default: true
    EnableUpdate = true,         // Default: true
    EnableDelete = true)]        // Default: true
```

### CrudFieldAttribute Properties

```csharp
[CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update,
    ApiName = "email",           // Override API name
    RequiredOnCreate = true,     // Validation
    Immutable = false,           // Cannot change on PATCH
    Hidden = false,              // Never exposed
    MinLength = 1,               // String validation
    MaxLength = 255,             // String validation
    Min = 0,                     // Numeric validation
    Max = 100,                   // Numeric validation
    Regex = @"^[\w@.]+$")]       // Pattern validation
```

### CrudRelationAttribute Properties

```csharp
[CrudRelation(
    Kind = RelationKind.ManyToOne,    // Optional; can be inferred
    ReadExpandAllowed = true,          // Can use expand=relation
    DefaultExpanded = false,           // Auto-expand
    WriteMode = RelationWriteMode.ById,
    WriteFieldName = "authorId",       // Field for writes
    RequiredOnCreate = false,
    ForeignKeyProperty = "AuthorId")]  // CLR FK property
```

### Default Mapping Rules

| Scenario | Default Behavior |
|----------|------------------|
| Property without `[CrudField]` | **Not exposed** (safe default) |
| Navigation without `[CrudRelation]` | Not included in writes; not expanded |
| Property with `[CrudHidden]` | Hard-hidden from all shapes |
| Property with `[CrudIgnore]` | Excluded from contract generation |

---

## 5. API Surface Specification

### Endpoint Patterns

Base prefix: `/api` (configurable via `DataSurfaceHttpOptions.ApiPrefix`)

| Method | Route | Operation | Description |
|--------|-------|-----------|-------------|
| `GET` | `/api/{route}` | List | Paginated list with filtering/sorting |
| `HEAD` | `/api/{route}` | List | Count only (X-Total-Count header) |
| `GET` | `/api/{route}/{id}` | Get | Single resource |
| `POST` | `/api/{route}` | Create | Create new resource |
| `PATCH` | `/api/{route}/{id}` | Update | Partial update |
| `DELETE` | `/api/{route}/{id}` | Delete | Delete resource |
| `POST` | `/api/{route}/bulk` | Bulk | Batch operations |
| `GET` | `/api/{route}/stream` | Stream | NDJSON streaming |
| `GET` | `/api/$schema/{route}` | Schema | JSON Schema for resource |
| `GET` | `/api/$resources` | Discovery | List available resources |

### Query Parameters

| Parameter | Example | Description |
|-----------|---------|-------------|
| `page` | `?page=2` | Page number (1-based, default: 1) |
| `pageSize` | `?pageSize=50` | Items per page (default: 20, max: contract-defined) |
| `sort` | `?sort=title,-createdAt` | Comma-separated, `-` for descending |
| `filter[field]` | `?filter[price]=gt:100` | Field filtering with operators |
| `expand` | `?expand=author,tags` | Include related resources |

### Filter Operators

| Operator | Example | Description |
|----------|---------|-------------|
| `eq` | `filter[status]=eq:active` | Equals (default if omitted) |
| `neq` | `filter[status]=neq:deleted` | Not equals |
| `gt` | `filter[price]=gt:100` | Greater than |
| `gte` | `filter[price]=gte:100` | Greater than or equal |
| `lt` | `filter[price]=lt:50` | Less than |
| `lte` | `filter[price]=lte:50` | Less than or equal |
| `contains` | `filter[name]=contains:john` | String contains |
| `starts` | `filter[name]=starts:john` | String starts with |
| `ends` | `filter[name]=ends:son` | String ends with |
| `in` | `filter[status]=in:a\|b\|c` | In list (pipe-separated) |

### Response Format

**List Response:**
```json
{
  "items": [...],
  "page": 1,
  "pageSize": 20,
  "total": 123
}
```

**Response Headers (List/HEAD):**
```http
X-Total-Count: 123
X-Page: 1
X-Page-Size: 20
```

**Get Response:** Single JSON object with optional ETag header.

---

## 6. Error Responses

DataSurface uses RFC 7807 Problem Details format:

```json
{
  "type": "https://datasurface/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "traceId": "00-abc123...",
  "errors": {
    "title": ["Max length is 200"],
    "userId": ["Required"]
  }
}
```

### Error Types

| Type | Status | Description |
|------|--------|-------------|
| `validation` | 400 | Request validation failed |
| `not-found` | 404 | Resource not found |
| `unauthorized` | 401 | Authentication required |
| `forbidden` | 403 | Authorization denied |
| `conflict` | 409 | Concurrency conflict |
| `invalid-metadata` | 500 | Contract configuration error (startup only) |

---

## 7. Safety Defaults

These defaults are enforced unless explicitly relaxed:

| Rule | Default Behavior |
|------|------------------|
| **Opt-in exposure** | Only `[CrudResource]` classes become endpoints |
| **Field allowlist** | Only annotated fields are accepted/emitted |
| **Unknown field rejection** | Unknown fields in requests → 400 |
| **No nested writes** | Relations written by ID only (`ById`/`ByIdList`) |
| **Controlled expansion** | Allowlist + depth limit (default: 1) |
| **Required pagination** | All lists are paged (default: 20, max: 200) |
| **Filter/sort allowlists** | Only explicitly allowed fields |
| **Startup validation** | Invalid contracts fail fast with diagnostics |

---

## JSON Contract Example

Complete contract representation (for debugging/dynamic definitions):

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
    "defaultSort": "-createdAt"
  },
  "read": {
    "expandAllowed": ["author", "tags"],
    "maxExpandDepth": 1,
    "defaultExpand": []
  },
  "operations": {
    "List": { "enabled": true, "outputShape": ["id", "title", "authorId", "createdAt"] },
    "Get": { "enabled": true, "outputShape": ["id", "title", "content", "authorId", "createdAt"] },
    "Create": {
      "enabled": true,
      "inputShape": ["title", "content", "authorId"],
      "requiredOnCreate": ["title", "authorId"]
    },
    "Update": {
      "enabled": true,
      "inputShape": ["title", "content"],
      "immutableFields": ["id", "authorId"],
      "concurrency": { "mode": "RowVersion", "fieldApiName": "rowVersion", "requiredOnUpdate": true }
    },
    "Delete": { "enabled": true }
  },
  "fields": [
    { "name": "Id", "apiName": "id", "type": "Int32", "inRead": true, "filterable": true, "sortable": true, "immutable": true },
    { "name": "Title", "apiName": "title", "type": "String", "inRead": true, "inCreate": true, "inUpdate": true, "filterable": true, "sortable": true, "validation": { "requiredOnCreate": true, "maxLength": 200 } },
    { "name": "Content", "apiName": "content", "type": "String", "nullable": true, "inRead": true, "inCreate": true, "inUpdate": true },
    { "name": "AuthorId", "apiName": "authorId", "type": "Int32", "inRead": true, "inCreate": true, "filterable": true, "validation": { "requiredOnCreate": true } },
    { "name": "CreatedAt", "apiName": "createdAt", "type": "DateTime", "inRead": true, "sortable": true }
  ],
  "relations": [
    {
      "name": "Author", "apiName": "author", "kind": "ManyToOne", "targetResourceKey": "User",
      "read": { "expandAllowed": true, "defaultExpanded": false },
      "write": { "mode": "ById", "writeFieldName": "authorId", "requiredOnCreate": true }
    },
    {
      "name": "Tags", "apiName": "tags", "kind": "ManyToMany", "targetResourceKey": "Tag",
      "read": { "expandAllowed": true, "defaultExpanded": false },
      "write": { "mode": "ByIdList", "writeFieldName": "tagIds", "requiredOnCreate": false }
    }
  ],
  "security": {
    "policies": { "List": null, "Get": null, "Create": "Authenticated", "Update": "Authenticated", "Delete": "Admin" }
  }
}

