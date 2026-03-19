# Enums & Types Reference

All enumerations and canonical types used in DataSurface contracts and attributes.

---

## CrudDto (Flags)

Field participation in DTO shapes and query capabilities. Combinable via bitwise OR.

| Flag | Value | Description |
|------|-------|-------------|
| `None` | 0 | Not included in any shape |
| `Read` | 1 | Included in GET responses |
| `Create` | 2 | Accepted in POST body |
| `Update` | 4 | Accepted in PATCH body |
| `Filter` | 8 | Can use `filter[field]=value` |
| `Sort` | 16 | Can use `sort=field` |

**Common combinations:**

| Pattern | Flags | Meaning |
|---------|-------|---------|
| Full lifecycle | `Read \| Create \| Update` | Read, create, and update |
| Read-only | `Read` | Only in responses |
| Queryable | `Read \| Filter \| Sort` | Readable and queryable |
| Full | `Read \| Create \| Update \| Filter \| Sort` | Everything enabled |

---

## CrudOperation

CRUD operation identifiers used in authorization, hooks, overrides, and contracts.

| Value | Description |
|-------|-------------|
| `List` | GET collection (paginated list) |
| `Get` | GET single item by ID |
| `Create` | POST new item |
| `Update` | PATCH / PUT existing item |
| `Delete` | DELETE item |

---

## StorageBackend

Backend storage types for resources.

| Value | Description |
|-------|-------------|
| `EfCore` | Entity Framework Core — static, compile-time entities |
| `DynamicJson` | JSON document storage — flexible, schema-free |
| `DynamicEav` | Entity-Attribute-Value storage — sparse data |
| `DynamicHybrid` | Hybrid — structured columns with JSON overflow |

---

## FieldType

Canonical field types mapping CLR types to contract representations.

| Value | C# Type | JSON Type |
|-------|---------|-----------|
| `String` | `string` | `string` |
| `Int32` | `int` | `integer` (format: int32) |
| `Int64` | `long` | `integer` (format: int64) |
| `Decimal` | `decimal` | `number` |
| `Boolean` | `bool` | `boolean` |
| `DateTime` | `DateTime` | `string` (format: date-time) |
| `Guid` | `Guid` | `string` (format: uuid) |
| `Json` | `JsonNode` / `JsonObject` | `object` |
| `Enum` | Enum types | `string` |
| `StringArray` | `string[]` | `array` of `string` |
| `IntArray` | `int[]` | `array` of `integer` |
| `GuidArray` | `Guid[]` | `array` of `string` (format: uuid) |
| `DecimalArray` | `decimal[]` | `array` of `number` |

---

## RelationKind

Relationship cardinality between resources.

| Value | Description | Example |
|-------|-------------|---------|
| `ManyToOne` | FK reference to a single entity | `Post.Author` → `User` |
| `OneToMany` | Collection of dependent entities | `User.Posts` → `Post[]` |
| `ManyToMany` | Junction table relationship | `Post.Tags` → `Tag[]` |
| `OneToOne` | 1:1 relationship | `User.Profile` → `Profile` |

---

## RelationWriteMode

How relation writes are performed on create and update operations.

| Value | Description | Request Format |
|-------|-------------|----------------|
| `None` | No write support | *(field not accepted)* |
| `ById` | Write via FK field | `{"authorId": 5}` |
| `ByIdList` | Write via ID array | `{"tagIds": [1, 2, 3]}` |
| `NestedDisabled` | Nested objects explicitly rejected | Returns 400 |

---

## ConcurrencyMode

Optimistic concurrency control mechanism.

| Value | Description |
|-------|-------------|
| `None` | No concurrency control |
| `RowVersion` | `byte[]` row version token (EF Core concurrency token) |
| `ETag` | HTTP ETag-based token |
