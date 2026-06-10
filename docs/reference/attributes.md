# Attributes Reference

All annotation attributes used to define DataSurface resources, fields, relationships, and behavior.

---

## `[CrudResource]`

Marks a class as a CRUD resource. Applied to the entity class.

```csharp
[CrudResource("users",
    ResourceKey = "User",
    Backend = StorageBackend.EfCore,
    KeyProperty = "Id",
    MaxPageSize = 200,
    MaxExpandDepth = 1,
    DefaultSort = "-createdAt",
    EnableList = true,
    EnableGet = true,
    EnableCreate = true,
    EnableUpdate = true,
    EnableDelete = true)]
public class User { }
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `route` *(positional)* | `string` | *(required)* | URL segment (e.g., `"users"`) |
| `ResourceKey` | `string` | Class name | Stable identifier used in contracts and hooks |
| `Backend` | `StorageBackend` | `EfCore` | Storage backend type |
| `KeyProperty` | `string` | *(discovered)* | Override primary key property discovery (`[CrudKey]` → `Id` → `{TypeName}Id`, case-insensitive) |
| `MaxPageSize` | `int` | `200` | Maximum items per page for list queries |
| `MaxExpandDepth` | `int` | `1` | Maximum relation expansion depth |
| `DefaultSort` | `string?` | `null` | Sort applied to list queries when the client sends none (e.g., `"-createdAt,name"`); fields must be sortable. The key is always appended as a final tie-breaker for deterministic paging |
| `EnableList` | `bool` | `true` | Enable `GET /api/{route}` |
| `EnableGet` | `bool` | `true` | Enable `GET /api/{route}/{id}` |
| `EnableCreate` | `bool` | `true` | Enable `POST /api/{route}` |
| `EnableUpdate` | `bool` | `true` | Enable `PATCH /api/{route}/{id}` |
| `EnableDelete` | `bool` | `true` | Enable `DELETE /api/{route}/{id}` |

---

## `[CrudKey]`

Marks the primary key property.

```csharp
[CrudKey(ApiName = "id")]
public int Id { get; set; }
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ApiName` | `string` | Property name (camelCase) | Override the API-facing key name |

Supported key types: `int`, `long`, `Guid`, `string`.

---

## `[CrudField]`

Controls field visibility, behavior, and validation.

```csharp
[CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort,
    ApiName = "email",
    RequiredOnCreate = true,
    Immutable = false,
    Hidden = false,
    Searchable = false,
    DefaultValue = null,
    ComputedExpression = null,
    AllowedValues = null,
    MinLength = null,
    MaxLength = null,
    Min = null,
    Max = null,
    Regex = null)]
public string Email { get; set; } = default!;
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `dto` *(positional)* | `CrudDto` | *(required)* | Flags controlling DTO inclusion and query capabilities |
| `ApiName` | `string` | Property name (camelCase) | Override the API-facing field name |
| `RequiredOnCreate` | `bool` | `false` | Field must be present on POST |
| `Immutable` | `bool` | `false` | Field rejected on PATCH (set once on create) |
| `Hidden` | `bool` | `false` | Hard-hidden — never exposed in any shape |
| `Searchable` | `bool` | `false` | Include in full-text search (`?q=`) |
| `DefaultValue` | `object?` | `null` | Default value applied when field is omitted on create |
| `ComputedExpression` | `string?` | `null` | Server-calculated expression (makes field read-only) |
| `AllowedValues` | `string?` | `null` | Pipe-separated allowed values (e.g., `"Active\|Inactive"`) |
| `MinLength` | `int?` | `null` | Minimum string length |
| `MaxLength` | `int?` | `null` | Maximum string length |
| `Min` | `decimal?` | `null` | Minimum numeric value |
| `Max` | `decimal?` | `null` | Maximum numeric value |
| `Regex` | `string?` | `null` | Regular expression pattern |

### CrudDto Flags

| Flag | Value | Effect |
|------|-------|--------|
| `None` | 0 | Not included in any shape |
| `Read` | 1 | Included in GET responses |
| `Create` | 2 | Accepted in POST body |
| `Update` | 4 | Accepted in PATCH body |
| `Filter` | 8 | Can be used in `filter[field]=value` |
| `Sort` | 16 | Can be used in `sort=field` |

Flags are combinable: `CrudDto.Read | CrudDto.Create | CrudDto.Update | CrudDto.Filter | CrudDto.Sort`

---

## `[CrudRelation]`

Configures navigation property behavior for reads and writes.

```csharp
[CrudRelation(
    Kind = RelationKind.ManyToOne,
    ReadExpandAllowed = true,
    DefaultExpanded = false,
    WriteMode = RelationWriteMode.ById,
    WriteFieldName = "authorId",
    RequiredOnCreate = false,
    ForeignKeyProperty = "AuthorId")]
public User Author { get; set; } = default!;
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Kind` | `RelationKind` | *(inferred)* | Cardinality: `ManyToOne`, `OneToMany`, `ManyToMany`, `OneToOne` |
| `ReadExpandAllowed` | `bool` | `false` | Can use `?expand=relation` |
| `DefaultExpanded` | `bool` | `false` | Automatically expanded without requesting |
| `WriteMode` | `RelationWriteMode` | `NestedDisabled` | How writes are performed |
| `WriteFieldName` | `string?` | `null` | API field name for writes (e.g., `"authorId"`) |
| `RequiredOnCreate` | `bool` | `false` | Write field required on POST |
| `ForeignKeyProperty` | `string?` | `null` | CLR FK property name |

### RelationWriteMode

| Value | Description |
|-------|-------------|
| `None` | No write support |
| `ById` | Write via FK field (e.g., `{"authorId": 5}`) |
| `ByIdList` | Write via ID array (e.g., `{"tagIds": [1,2,3]}`) |
| `NestedDisabled` | Nested objects explicitly rejected |

---

## `[CrudConcurrency]`

Marks a row version property for optimistic concurrency.

```csharp
[CrudConcurrency(RequiredOnUpdate = true)]
public byte[] RowVersion { get; set; } = default!;
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Mode` | `ConcurrencyMode` | `RowVersion` | Concurrency mechanism (`None`, `RowVersion`, `ETag`) |
| `RequiredOnUpdate` | `bool` | `true` | Whether `If-Match` header is required on PATCH/PUT |

If the token property has no `[CrudField]` of its own, it is automatically exposed as a
read-only field so clients can obtain the token.

---

## `[CrudAuthorize]`

Sets authorization policies per operation. Can be applied multiple times. Class-wide policies
(no `Operation`) are applied first, so per-operation policies always win.

```csharp
[CrudAuthorize("AdminOnly")]
[CrudAuthorize("SuperAdmin", Operation = CrudOperation.Delete)]
public class User { }
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `policy` *(positional)* | `string` | *(required)* | ASP.NET Core authorization policy name |
| `Operation` | `CrudOperation` | *(unset — all operations)* | Specific operation; `HasOperation` reports whether it was set |

---

## `[CrudTenant]`

Marks a property as the tenant discriminator for automatic multi-tenancy.

```csharp
[CrudTenant(ClaimType = "tenant_id", Required = true)]
public string TenantId { get; set; } = default!;
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ClaimType` | `string` | `"tenant_id"` | Claim type to extract tenant ID from |
| `Required` | `bool` | `true` | Reject requests without the tenant claim; if `false`, operations proceed without tenant filtering |

The tenant field is always server-managed: it is forced read-only in the contract and can never
be set by clients, regardless of `[CrudField]` flags.

---

## `[CrudHidden]`

Completely hides a property from the contract. The field is never exposed in any DTO shape.

```csharp
[CrudHidden]
public string InternalSecret { get; set; } = default!;
```

No properties. Apply to any property that should be invisible to the API.

---

## `[CrudIgnore]`

Excludes a property from contract generation entirely. Use for EF navigation properties or internal properties that should not be processed by DataSurface at all.

```csharp
[CrudIgnore]
public ICollection<Post> Posts { get; set; } = new List<Post>();
```

No properties. Differs from `[CrudHidden]` in that the property is not included in the contract at all, whereas hidden fields are included but marked as hidden.

---

## Default Mapping Rules

| Scenario | Default Behavior |
|----------|------------------|
| Property without `[CrudField]` | **Not exposed** via API (safe default) |
| Navigation without `[CrudRelation]` | Not included in writes, not expanded |
| Property with `[CrudHidden]` | Hard-hidden from all shapes |
| Property with `[CrudIgnore]` | Excluded from contract generation |
