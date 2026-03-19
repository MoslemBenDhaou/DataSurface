# Validation

DataSurface provides comprehensive built-in validation driven by the ResourceContract. Validation rules are declared via `[CrudField]` attributes and enforced automatically on create and update operations.

---

## Validation Rules

| Rule | Attribute Property | Applies To | Description |
|------|--------------------|-----------|-------------|
| Required on create | `RequiredOnCreate = true` | POST | Field must be present in the request body |
| Immutable | `Immutable = true` | PATCH | Field is rejected on update — can only be set on create |
| Min length | `MinLength = N` | POST, PATCH | Minimum string length |
| Max length | `MaxLength = N` | POST, PATCH | Maximum string length |
| Min value | `Min = N` | POST, PATCH | Minimum numeric value |
| Max value | `Max = N` | POST, PATCH | Maximum numeric value |
| Regex pattern | `Regex = "..."` | POST, PATCH | Value must match the regular expression |
| Allowed values | `AllowedValues = "a\|b\|c"` | POST, PATCH | Value must be one of the pipe-separated options |
| Unknown field rejection | *(automatic)* | POST, PATCH | Fields not in the contract are rejected |

---

## Example

```csharp
[CrudResource("users")]
public class User
{
    [CrudKey]
    public int Id { get; set; }

    // Required on create, with regex pattern
    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update,
        RequiredOnCreate = true,
        Regex = @"^[\w.+-]+@[\w-]+\.[\w.]+$")]
    public string Email { get; set; } = default!;

    // Immutable — set once on create, rejected on PATCH
    [CrudField(CrudDto.Read | CrudDto.Create, Immutable = true)]
    public string Username { get; set; } = default!;

    // String length validation
    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update,
        MinLength = 8, MaxLength = 100)]
    public string Password { get; set; } = default!;

    // Numeric range validation
    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update,
        Min = 0, Max = 150)]
    public int Age { get; set; }

    // Regex pattern validation
    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update,
        Regex = @"^\+?[1-9]\d{1,14}$")]
    public string? PhoneNumber { get; set; }

    // Allowed values — enum-like restriction
    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update,
        AllowedValues = "Active|Inactive|Pending")]
    public string Status { get; set; } = default!;
}
```

---

## Validation Behavior

### On Create (POST)

1. Unknown fields → rejected
2. `RequiredOnCreate` fields → must be present
3. String length checks → `MinLength`, `MaxLength`
4. Numeric range checks → `Min`, `Max`
5. Pattern matching → `Regex`
6. Value restriction → `AllowedValues`

### On Update (PATCH)

1. Unknown fields → rejected
2. `Immutable` fields → rejected (cannot be changed)
3. Concurrency token → required if `RequiredOnUpdate = true`
4. String length checks → on provided fields
5. Numeric range checks → on provided fields
6. Pattern matching → on provided fields
7. Value restriction → on provided fields

Validation only runs on fields actually present in the request body. Omitted fields on PATCH are left unchanged and not validated.

---

## Error Response

Validation errors return HTTP 400 with RFC 7807 Problem Details:

```json
{
  "type": "https://datasurface/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "traceId": "00-abc123...",
  "errors": {
    "email": ["Required"],
    "password": ["Min length is 8"],
    "age": ["Must be between 0 and 150"],
    "status": ["Must be one of: Active, Inactive, Pending"]
  }
}
```

Multiple validation errors are collected and returned together — the response includes all failures, not just the first one.

---

## Feature Flag

Validation can be disabled via feature flags:

```csharp
opt.Features = new DataSurfaceFeatures
{
    EnableFieldValidation = false  // Disables MinLength, MaxLength, Min, Max, Regex, AllowedValues
};
```

Note: `RequiredOnCreate`, `Immutable`, and unknown field rejection are always enforced regardless of the feature flag.

---

## Related

- [Error Responses Reference](../reference/error-responses.md) — All error types and status codes
- [Attributes Reference](../reference/attributes.md) — Full `[CrudField]` property list
