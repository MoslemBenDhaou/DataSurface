# Querying

DataSurface provides a rich query system for filtering, sorting, searching, paginating, and projecting fields — all driven by the ResourceContract.

---

## Pagination

All list responses are paginated. Pagination is mandatory and cannot be disabled.

| Parameter | Example | Default | Description |
|-----------|---------|---------|-------------|
| `page` | `?page=2` | 1 | Page number (1-based) |
| `pageSize` | `?pageSize=50` | 20 | Items per page |

Maximum page size is controlled per resource via `MaxPageSize` on `[CrudResource]` (default: 200). Requests exceeding the maximum are clamped to the maximum.

### Response Format

```json
{
  "items": [...],
  "page": 1,
  "pageSize": 20,
  "total": 142
}
```

### Response Headers

List endpoints include pagination headers:

```http
X-Total-Count: 142
X-Page: 1
X-Page-Size: 20
```

---

## Filtering

Filter results using `filter[field]=operator:value` query parameters. Only fields with the `CrudDto.Filter` flag can be filtered.

### Operators

| Operator | Example | Description |
|----------|---------|-------------|
| `eq` | `filter[status]=eq:active` | Equals (default if operator omitted) |
| `neq` | `filter[status]=neq:deleted` | Not equals |
| `gt` | `filter[price]=gt:100` | Greater than |
| `gte` | `filter[price]=gte:100` | Greater than or equal |
| `lt` | `filter[price]=lt:50` | Less than |
| `lte` | `filter[price]=lte:50` | Less than or equal |
| `contains` | `filter[name]=contains:john` | String contains (case-insensitive) |
| `starts` | `filter[name]=starts:john` | String starts with |
| `ends` | `filter[name]=ends:son` | String ends with |
| `in` | `filter[status]=in:a\|b\|c` | In list (pipe-separated values) |
| `isnull` | `filter[email]=isnull:true` | Is null / is not null |

When the operator is omitted, `eq` is assumed:

```
?filter[status]=active       # same as filter[status]=eq:active
```

### Multiple Filters

Multiple filters are combined with AND logic:

```
?filter[status]=active&filter[price]=gt:100
```

This returns items where `status = active` AND `price > 100`.

### Enabling Filtering on Fields

```csharp
[CrudField(CrudDto.Read | CrudDto.Filter)]
public string Status { get; set; } = default!;

[CrudField(CrudDto.Read | CrudDto.Filter | CrudDto.Sort)]
public decimal Price { get; set; }
```

Filters on non-filterable fields are silently ignored.

---

## Sorting

Sort results using the `sort` query parameter. Only fields with the `CrudDto.Sort` flag can be sorted.

```
?sort=title,-createdAt
```

- Comma-separated field names
- Prefix with `-` for descending order
- Multiple sort fields are applied in order (primary, secondary, etc.)

### Default Sort

Set a default sort order per resource:

```csharp
[CrudResource("posts", DefaultSort = "-createdAt")]
public class Post { /* ... */ }
```

The default sort applies when no `sort` parameter is provided.

Sorts on non-sortable fields are silently ignored.

---

## Full-Text Search

Search across multiple fields using the `q` parameter:

```
?q=john
```

This searches all fields marked with `Searchable = true`:

```csharp
[CrudField(CrudDto.Read | CrudDto.Filter, Searchable = true)]
public string Title { get; set; } = default!;

[CrudField(CrudDto.Read, Searchable = true)]
public string Description { get; set; } = default!;
```

The search performs case-insensitive `LIKE '%term%'` across all searchable fields, combined with OR logic. Search can be combined with filters:

```
?q=john&filter[status]=active
```

---

## Field Projection

Select specific fields to return using the `fields` parameter:

```
?fields=id,email,name
```

### Response

```json
{
  "items": [
    { "id": 1, "email": "john@example.com", "name": "John" },
    { "id": 2, "email": "jane@example.com", "name": "Jane" }
  ]
}
```

- Comma-separated list of field API names
- Only requested fields are included in the response
- Invalid field names are ignored
- Works with both list (`GET /api/{resource}`) and single (`GET /api/{resource}/{id}`) endpoints
- Requires `EnableFieldProjection = true` in feature flags (enabled by default)

---

## Expansion

Include related resources using the `expand` parameter:

```
?expand=author,tags
```

### Configuration

Relations must be explicitly marked as expandable:

```csharp
[CrudRelation(ReadExpandAllowed = true)]
public User Author { get; set; } = default!;
```

### Depth Limit

Maximum expansion depth is configurable per resource:

```csharp
[CrudResource("posts", MaxExpandDepth = 2)]
public class Post { /* ... */ }
```

Default depth limit is 1.

### Default Expansion

Relations can be expanded automatically without requesting them:

```csharp
[CrudRelation(ReadExpandAllowed = true, DefaultExpanded = true)]
public User Author { get; set; } = default!;
```

See [Relationships](relationships.md) for full relation configuration.

---

## Combining Query Parameters

All query parameters can be combined:

```
GET /api/posts?page=1&pageSize=25&sort=-createdAt&filter[status]=published&q=tutorial&expand=author&fields=id,title,author
```

This request:
1. Filters to published posts
2. Searches for "tutorial" in searchable fields
3. Sorts by newest first
4. Expands the author relation
5. Returns only id, title, and author fields
6. Returns page 1 with 25 items per page

---

## Related

- [Validation](validation.md) — How input validation works
- [Relationships](relationships.md) — Expansion and relation writes
- [API Endpoints Reference](../reference/api-endpoints.md) — Full endpoint specification
