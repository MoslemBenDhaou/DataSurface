# Relationships

DataSurface supports navigation property relationships between resources, with configurable expansion (read) and write behavior — all declared via the `[CrudRelation]` attribute.

---

## Defining Relationships

```csharp
[CrudResource("posts")]
public class Post
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Filter)]
    public int AuthorId { get; set; }

    [CrudRelation(
        ReadExpandAllowed = true,
        WriteMode = RelationWriteMode.ById,
        WriteFieldName = "authorId",
        RequiredOnCreate = true)]
    public User Author { get; set; } = default!;

    [CrudRelation(
        Kind = RelationKind.ManyToMany,
        ReadExpandAllowed = true,
        WriteMode = RelationWriteMode.ByIdList,
        WriteFieldName = "tagIds")]
    public List<Tag> Tags { get; set; } = new();
}
```

---

## Relation Kinds

| Kind | Description | Example |
|------|-------------|---------|
| `ManyToOne` | FK reference to a single entity | `Post.Author` |
| `OneToMany` | Collection of dependent entities | `User.Posts` |
| `ManyToMany` | Junction table relationship | `Post.Tags` |
| `OneToOne` | 1:1 relationship | `User.Profile` |

The `Kind` can often be inferred from the property type. Specify it explicitly when inference is ambiguous.

---

## Read Behavior — Expansion

### Enabling Expansion

```csharp
[CrudRelation(ReadExpandAllowed = true)]
public User Author { get; set; } = default!;
```

Clients can then request expansion:

```
GET /api/posts?expand=author
```

### Default Expansion

Automatically expand a relation without the client requesting it:

```csharp
[CrudRelation(ReadExpandAllowed = true, DefaultExpanded = true)]
public User Author { get; set; } = default!;
```

### Depth Limit

Maximum expansion depth is configurable per resource to prevent deep recursive loading:

```csharp
[CrudResource("posts", MaxExpandDepth = 2)]
public class Post { /* ... */ }
```

Default is 1. This means expanding `author.posts.author` would be rejected if depth limit is 1.

### Multiple Expansions

```
GET /api/posts?expand=author,tags
```

Only relations with `ReadExpandAllowed = true` can be expanded. Non-expandable relations are silently ignored.

---

## Write Behavior

### Write Modes

| Mode | Description | Request Format |
|------|-------------|----------------|
| `None` | No write support | *(field not accepted)* |
| `ById` | Write via FK field | `{"authorId": 5}` |
| `ByIdList` | Write via ID array | `{"tagIds": [1, 2, 3]}` |
| `NestedDisabled` | Nested objects explicitly rejected | Returns 400 if nested object sent |

### ById — Single FK Reference

For `ManyToOne` relations, write the FK value directly:

```csharp
[CrudRelation(WriteMode = RelationWriteMode.ById, WriteFieldName = "authorId")]
public User Author { get; set; } = default!;
```

**Create request:**
```json
{
  "title": "My Post",
  "authorId": 5
}
```

### ByIdList — Many-to-Many

For `ManyToMany` relations, write an array of IDs:

```csharp
[CrudRelation(WriteMode = RelationWriteMode.ByIdList, WriteFieldName = "tagIds")]
public List<Tag> Tags { get; set; } = new();
```

**Create request:**
```json
{
  "title": "My Post",
  "tagIds": [1, 2, 3]
}
```

### Required on Create

Make a relation required when creating a resource:

```csharp
[CrudRelation(WriteMode = RelationWriteMode.ById, RequiredOnCreate = true)]
public User Author { get; set; } = default!;
```

A missing `authorId` on POST returns 400.

### Foreign Key Property

Explicitly specify the CLR foreign key property when inference is insufficient:

```csharp
[CrudRelation(
    WriteMode = RelationWriteMode.ById,
    WriteFieldName = "authorId",
    ForeignKeyProperty = "AuthorId")]
public User Author { get; set; } = default!;
```

---

## Safety Defaults

| Rule | Default |
|------|---------|
| Properties without `[CrudRelation]` | Not included in writes, not expanded |
| Write mode default | `None` — no writes unless explicitly enabled |
| Nested object writes | Not allowed — relations are written by ID only |
| Expansion | Requires `ReadExpandAllowed = true` |
| Depth | Max 1 level by default |

---

## Contract Representation

In the `ResourceContract`, relations are represented as `RelationContract`:

```json
{
  "name": "Author",
  "apiName": "author",
  "kind": "ManyToOne",
  "targetResourceKey": "User",
  "read": { "expandAllowed": true, "defaultExpanded": false },
  "write": { "mode": "ById", "writeFieldName": "authorId", "requiredOnCreate": true }
}
```

---

## Related

- [Querying — Expansion](querying.md#expansion) — How clients request expansion
- [Contracts](../architecture/contracts.md) — RelationContract schema details
- [Attributes Reference](../reference/attributes.md) — `[CrudRelation]` properties
