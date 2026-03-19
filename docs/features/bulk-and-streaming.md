# Bulk Operations, Streaming & Import/Export

DataSurface supports batch operations, async streaming, and data import/export for handling large datasets efficiently.

---

## Bulk Operations

Batch create, update, and delete operations in a single request via `POST /api/{resource}/bulk`.

### Setup

```csharp
builder.Services.AddScoped<IDataSurfaceBulkService, EfDataSurfaceBulkService>();
```

### Request Format

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

### Options

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `create` | array | `[]` | Records to create |
| `update` | array | `[]` | Records to update (id + patch fields) |
| `delete` | array | `[]` | IDs to delete |
| `stopOnError` | bool | `true` | Stop processing on first error |
| `useTransaction` | bool | `true` | Wrap all operations in a database transaction |

### Behavior

- All validation, hooks, and security checks apply to each individual operation
- When `useTransaction = true`, a failure rolls back all changes
- When `stopOnError = false`, errors are collected and returned without halting

---

## Async Streaming

Stream large datasets via `GET /api/{resource}/stream` using NDJSON (newline-delimited JSON) format.

### Setup

```csharp
builder.Services.AddScoped<IDataSurfaceStreamingService, EfDataSurfaceStreamingService>();
```

### Usage

**HTTP:**
```http
GET /api/users/stream?filter[status]=active
```

**Response** (NDJSON — one JSON object per line):
```
{"id":1,"name":"User 1","email":"user1@example.com"}
{"id":2,"name":"User 2","email":"user2@example.com"}
{"id":3,"name":"User 3","email":"user3@example.com"}
```

**Programmatic (C#):**
```csharp
await foreach (var item in streamingService.StreamAsync("User", querySpec))
{
    // Process each item as it arrives
}
```

### Characteristics

- Uses `IAsyncEnumerable<T>` — items are sent as they are read from the database
- No pagination — streams the entire result set
- Filters, sorting, and security checks apply as with normal list queries
- Ideal for large exports, data migration, and ETL pipelines
- Content-Type: `application/x-ndjson`

---

## Import/Export

Bulk data import and export via dedicated endpoints.

### Enable

```csharp
app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
{
    EnableImportExport = true
});
```

### Export

```http
GET /api/users/export?format=json
GET /api/users/export?format=csv
```

| Format | Content-Type | Description |
|--------|-------------|-------------|
| `json` | `application/json` | JSON array of all records |
| `csv` | `text/csv` | CSV with headers matching API field names |

- Exports all records matching any applied query filters
- Security filters (tenant, row-level) are respected
- Field authorization redaction applies

### Import

```http
POST /api/users/import
Content-Type: application/json

[
  { "email": "user1@example.com", "name": "User 1" },
  { "email": "user2@example.com", "name": "User 2" }
]
```

### Import Response

```json
{
  "totalRecords": 2,
  "importedCount": 2,
  "failedCount": 0,
  "errors": []
}
```

### Import Options

| Option | Description |
|--------|-------------|
| `SkipValidation` | Skip field validation (use with caution) |
| `UpdateExisting` | Update records if they already exist (by key) |

### Import Behavior

- Each record is validated against the resource contract
- Validation errors are collected per record
- Hooks and security checks apply to each record
- Returns a summary of imported, failed, and skipped records

---

## Related

- [CRUD Operations](crud-operations.md) — Standard single-record operations
- [Querying](querying.md) — Filter parameters that apply to streaming and export
