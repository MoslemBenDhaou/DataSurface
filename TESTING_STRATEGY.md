# DataSurface Testing Strategy

## Why This Document Exists

Before writing a single test, we need to solve three problems that plague most test suites:

1. **Tests that validate the implementation, not the requirement** — "I wrote code that does X, let me assert it does X" gives you green tests even when X is wrong.
2. **Partial coverage that feels complete** — 90% line coverage means nothing if you only tested the happy path and every edge case is a production bug.
3. **Coverage as a vanity metric** — A test that asserts `options.EnableRateLimiting.Should().BeFalse()` adds to your coverage number but catches zero real bugs.

This document defines *what* we test, *why* we test it, and *how* we know we haven't missed anything — before any test code is written.

---

## Table of Contents

- [1. Current State Audit](#1-current-state-audit)
- [2. Core Principles](#2-core-principles)
- [3. Test Architecture](#3-test-architecture)
- [4. Test Categories & What They Cover](#4-test-categories--what-they-cover)
- [5. Scenario Matrix](#5-scenario-matrix)
- [6. Test Writing Rules](#6-test-writing-rules)
- [7. What NOT to Test](#7-what-not-to-test)
- [8. Execution Plan](#8-execution-plan)
- [9. CI Integration](#9-ci-integration)

---

## 1. Current State Audit

### What exists today

| Project | Files | Approximate Tests | What They Actually Test |
|---------|-------|-------------------|------------------------|
| `Tests.Unit/Core/` | 7 files | ~48 | Record/POCO constructors and property getters |
| `Tests.Unit/Http/` | 2 files | ~20 | Option defaults and `QuerySpec` record |
| `Tests.Unit/EFCore/` | 1 file | ~17 | `EfCrudQueryEngine.Apply()` with in-memory LINQ |
| `Tests.Integration/` | 2 files | ~19 | `ContractBuilder` and `QueryEngine` against EF InMemory |

### The problem

**~85 tests, but nearly all of them test "does the data container hold my data?"**

Example — this test from `CrudFieldAttributeTests`:
```csharp
var attr = new CrudFieldAttribute(CrudDto.Read);
attr.Searchable.Should().BeFalse();
```

This tests that C#'s default value for `bool` is `false`. It will never catch a real bug. It inflates coverage on `CrudFieldAttribute` but proves nothing about whether the `Searchable` flag actually makes a field searchable in a query.

### What is NOT tested at all

- **CRUD service operations** — zero tests for `CreateAsync`, `GetAsync`, `UpdateAsync`, `DeleteAsync`, `ListAsync`
- **Validation enforcement** — no test verifies that `RequiredOnCreate` actually rejects a missing field
- **Security pipeline** — no test for tenant isolation, row-level security, field authorization, field redaction
- **Hook execution** — no test verifies hooks fire in order, or that `BeforeCreate` can reject a request
- **HTTP layer** — zero `WebApplicationFactory` tests despite the package being referenced
- **Error responses** — no test verifies 404, 409, 412, 422 responses
- **Concurrency** — no ETag / `If-Match` tests
- **Mapper** — no test for JSON → entity or entity → JSON mapping
- **Dynamic CRUD** — zero tests for `DynamicDataSurfaceCrudService`
- **Negative paths** — almost no test checks "what happens when the input is wrong?"

---

## 2. Core Principles

### Principle 1: Test the requirement, not the implementation

Every test must answer a question a **user of the library** would ask, not a question about internal code structure.

| Bad (tests implementation) | Good (tests requirement) |
|---|---|
| "Does `FieldContract.Filterable` return `true` when I set it to `true`?" | "When I mark a field as filterable and send `?filter[status]=active`, does the API return only active records?" |
| "Does `ImportOptions.BatchSize` default to 100?" | "When I import 250 records with batch size 100, does the system process 3 batches and import all 250?" |
| "Does `WebhookEvent.Operation` return `Create`?" | "When I create a resource and `IWebhookPublisher` is registered, does it receive an event with the correct resource key, operation, and payload?" |

### Principle 2: Define expected behavior BEFORE looking at code

For each scenario, write the expected behavior based on the **README and contract documentation**, not by reading the implementation. If the implementation disagrees with the docs, that's a bug we want to catch — not a test we want to match to the code.

### Principle 3: Every test must be able to fail

If you can't imagine a realistic code change that would make the test fail (and that failure would represent a real bug), the test is useless. Delete it.

### Principle 4: Test boundaries, not internals

Focus on the **public API surface** — the interfaces, the HTTP endpoints, the extension methods. If a class is `internal` or `private`, it gets tested through its public consumer.

### Principle 5: Negative tests are not optional

For every happy-path test, there should be at least one negative test. "Create succeeds with valid data" is only half the story. "Create fails with missing required field and returns 422 with field-level error" is the other half.

---

## 3. Test Architecture

### Three layers, each with a clear purpose

```
┌─────────────────────────────────────────────────────────┐
│  HTTP / API Tests (DataSurface.Tests.Api)               │
│  WebApplicationFactory + real HTTP calls                 │
│  Tests: "As an API consumer, when I call X, I get Y"    │
│  Catches: routing, serialization, status codes, headers  │
│  DB: SQLite in-memory                                    │
├─────────────────────────────────────────────────────────┤
│  Service / Behavior Tests (DataSurface.Tests.Service)   │
│  Real services + EF InMemory/SQLite, mocked externals   │
│  Tests: "When the service does X, the result is Y"      │
│  Catches: business logic, validation, hooks, security    │
│  DB: EF InMemory or SQLite in-memory                     │
├─────────────────────────────────────────────────────────┤
│  Unit Tests (DataSurface.Tests.Unit) — REWORKED         │
│  Pure logic, no DB, no DI, no HTTP                      │
│  Tests: "Given input X, this function returns Y"        │
│  Catches: parsing, mapping, expression building, config  │
│  DB: none                                                │
└─────────────────────────────────────────────────────────┘
```

### Why three layers?

- **Unit tests** are fast (~ms each), run on every save, catch logic errors in isolated functions.
- **Service tests** verify that the real services (CRUD, hooks, validation, security) work together correctly without HTTP overhead. They catch integration bugs between components.
- **API tests** verify the full HTTP pipeline end-to-end. They catch serialization issues, routing bugs, missing middleware, wrong status codes.

### Project structure

```
DataSurface.Tests.Unit/           # Reworked — only pure-logic tests
  ContractBuilder/                # Contract building from types
  QueryEngine/                    # Filter/sort/page expression building
  Mapper/                         # JSON ↔ entity mapping
  Validation/                     # Field validation logic
  QueryParser/                    # HTTP query string parsing

DataSurface.Tests.Service/        # NEW — behavioral service tests
  Crud/                           # CRUD operations via service interface
  Security/                       # Authorization, tenant isolation, field redaction
  Hooks/                          # Hook execution order and side effects
  Caching/                        # Query cache hit/miss/invalidation
  Dynamic/                        # Dynamic entity CRUD operations
  BulkOperations/                 # Batch create/update/delete
  Concurrency/                    # ETag / optimistic concurrency
  ImportExport/                   # Import/export service behavior

DataSurface.Tests.Api/            # NEW — HTTP endpoint tests
  CrudEndpoints/                  # Full CRUD over HTTP
  ErrorResponses/                 # 400, 404, 409, 412, 422 responses
  QueryString/                    # Filter/sort/page/search via query params
  Headers/                        # ETag, If-Match, If-None-Match, Cache-Control
  Discovery/                      # /api/$discovery, /api/$schema endpoints
  Admin/                          # Admin API endpoints
  Security/                       # Auth policies, API keys, tenant headers

Shared/
  TestFixtures/                   # Shared entities, DbContext, builders
  Builders/                       # Fluent test data builders
```

---

## 4. Test Categories & What They Cover

### 4.1 Contract Building Tests (Unit)

**Question being answered:** "When I annotate my entity class with DataSurface attributes, does the `ContractBuilder` produce the correct `ResourceContract`?"

| Scenario | Expected Behavior |
|----------|-------------------|
| Entity with `[CrudResource("users")]` and `[CrudKey]` on `Id` | Contract has `Route = "users"`, `Key.Name = "Id"` |
| Field with `[CrudField(CrudDto.Read \| CrudDto.Create)]` | `InRead = true`, `InCreate = true`, `InUpdate = false` |
| Field with `RequiredOnCreate = true` | `Validation.RequiredOnCreate = true` |
| Field with `Immutable = true` | `Immutable = true`, and field is NOT in `CrudDto.Update` |
| Field with `ComputedExpression = "..."` | `Computed = true`, not in Create or Update |
| Field with `AllowedValues = "a\|b\|c"` | `Validation.AllowedValues = ["a", "b", "c"]` |
| Field with `[CrudIgnore]` | Field does not appear in contract |
| Field with `[CrudHidden]` | `Hidden = true` |
| Entity with `[CrudTenant]` on a property | `TenantContract` is populated |
| Entity with `[CrudRelation]` | `Relations` list is populated correctly |
| Entity with `[CrudConcurrency]` | `Concurrency` contract is set |
| Entity with NO `[CrudKey]` | Error/diagnostic reported |
| Entity with multiple `[CrudKey]` properties | Composite key or error |
| Field with `MinLength > MaxLength` | Error or contract built with invalid state (we decide) |
| Empty assembly scan | Returns empty list, no crash |

### 4.2 Query Engine Tests (Unit)

**Question being answered:** "Given a queryable dataset and a `QuerySpec`, does the engine produce the correct filtered/sorted/paged result?"

Already partially covered. **Gaps to fill:**

| Scenario | Expected Behavior |
|----------|-------------------|
| Filter with unknown operator (e.g., `foo:bar`) | Ignored or error — define and test the behavior |
| Filter with empty value (`eq:`) | Defined behavior — returns empty or all? |
| Filter on a field that doesn't exist in contract | Ignored (current) — but is this correct? |
| Sort on a field that doesn't exist in contract | Ignored (current) — but is this correct? |
| Multiple filters on the same field | Both applied (AND logic) or last wins? |
| `PageSize = 0` | Clamped to 1 |
| `PageSize = -1` | Clamped to 1 |
| `Page = -5` | Clamped to 1 |
| Search with special characters (`%`, `_`, `'`) | No SQL injection, defined behavior |
| Search with empty string | Returns all or none? |
| Filter with `in:` and single value | Works like `eq:` |
| Filter with `in:` and empty pipe (`in:a\|`) | Ignores empty segment |
| Sort with mixed valid and invalid fields (`title,-nonexistent`) | Valid fields applied, invalid ignored |
| DateTime filter with various formats | Defined format or error |
| Boolean filter with various values (`true`, `True`, `1`, `yes`) | Defined behavior |
| Decimal filter with locale-specific separators (`1,000.50`) | Defined behavior |

### 4.3 Validation Tests (Service)

**Question being answered:** "When I submit invalid data, does the system reject it with the correct error?"

| Scenario | Expected Behavior |
|----------|-------------------|
| Create with missing required field | Throws validation error with field name |
| Create with field value below `Min` | Throws validation error |
| Create with field value above `Max` | Throws validation error |
| Create with string shorter than `MinLength` | Throws validation error |
| Create with string longer than `MaxLength` | Throws validation error |
| Create with value not matching `Regex` | Throws validation error |
| Create with value not in `AllowedValues` | Throws validation error |
| Update with immutable field in payload | Throws or silently ignores — define and test |
| Create with extra fields not in contract | Silently ignored or error — define and test |
| Create with correct data | Succeeds, returns created entity |
| Update with valid partial payload | Succeeds, only specified fields change |
| Null value for non-nullable field | Throws validation error |
| Null value for nullable field | Succeeds |
| Empty string for required field | Throws or succeeds — define and test |
| Multiple validation errors at once | All errors returned, not just the first |

### 4.4 CRUD Service Tests (Service)

**Question being answered:** "Do the core CRUD operations produce the correct results and side effects?"

| Scenario | Expected Behavior |
|----------|-------------------|
| **List** — empty table | Returns `{ items: [], total: 0 }` |
| **List** — with data, default pagination | Returns first page, correct total |
| **List** — page beyond data | Returns empty items, correct total |
| **Get** — existing entity | Returns JSON with all readable fields |
| **Get** — non-existent ID | Returns `null` |
| **Get** — with expansion | Related entities included |
| **Create** — valid payload | Entity persisted, returned with server-set fields (Id, CreatedAt) |
| **Create** — with default values | Default values applied for missing fields |
| **Create** — with computed fields in payload | Computed fields ignored on input |
| **Update** — valid payload | Only provided fields change, others preserved |
| **Update** — non-existent ID | Throws not-found exception |
| **Update** — with immutable field | Rejected or ignored |
| **Delete** — existing entity | Entity removed (or soft-deleted) |
| **Delete** — non-existent ID | Throws not-found exception |
| **Delete** — soft-delete entity | `IsDeleted = true`, not physically removed |
| **Delete** — soft-deleted entity excluded from List | Doesn't appear in subsequent List |
| **List** — with field projection (`?fields=id,name`) | Only requested fields in response |
| **List** — with expand | Related entities included |
| Disabled operation (e.g., Delete disabled) | Throws operation-not-allowed |

### 4.5 Hook Tests (Service)

**Question being answered:** "Do lifecycle hooks fire at the right time, in the right order, with the right data?"

| Scenario | Expected Behavior |
|----------|-------------------|
| Global `BeforeAsync` fires before create | Hook receives context with `Operation = Create` |
| Global `AfterAsync` fires after create | Hook receives context after entity is saved |
| Typed `BeforeCreateAsync` receives entity and body | Entity is accessible, body is the original JSON |
| Typed `AfterCreateAsync` receives persisted entity | Entity has server-set fields (Id) |
| Multiple hooks execute in `Order` sequence | Lower order runs first |
| Hook throws exception | Operation aborted, error propagated |
| `BeforeUpdate` hook can inspect patch | Patch JSON is accessible |
| `AfterRead` hook can modify response | Modified JSON is what the caller receives |
| No hooks registered | CRUD operations work normally |
| Hooks fire for List (per item) | `AfterRead` called for each item in list |

### 4.6 Security Tests (Service)

**Question being answered:** "Does the security pipeline correctly restrict access?"

| Scenario | Expected Behavior |
|----------|-------------------|
| **Tenant isolation** — user with tenant A | Can only see tenant A's records |
| **Tenant isolation** — no tenant claim when required | Rejected |
| **Row-level security** — `IResourceFilter` restricts records | List/Get only returns allowed records |
| **Resource authorization** — unauthorized user | Throws forbidden |
| **Field authorization** — unauthorized field in response | Field redacted from JSON |
| **Field write authorization** — unauthorized field in create body | Rejected |
| **Audit log** — after CRUD operation | `IAuditLogger.LogAsync` called with correct entry |
| **Policy-based auth** — endpoint requires policy | Unauthenticated request returns 401 |
| **API key auth** — valid key | Request succeeds |
| **API key auth** — invalid key | Returns 401/403 |
| **API key auth** — missing key | Returns 401 |
| Security disabled via feature flags | No security checks applied |
| Cache bypassed when security is active | Different users don't see each other's cached data |

### 4.7 Concurrency Tests (Service)

**Question being answered:** "Does optimistic concurrency prevent lost updates?"

| Scenario | Expected Behavior |
|----------|-------------------|
| Update with correct `If-Match` ETag | Update succeeds |
| Update with stale `If-Match` ETag | Returns 412 Precondition Failed |
| Update with no `If-Match` header | Depends on config — test both modes |
| Concurrent updates to same entity | Second update fails with 409 or 412 |
| Get returns ETag in response | `ETag` header is present |
| Conditional GET with matching ETag | Returns 304 Not Modified |
| Conditional GET with stale ETag | Returns 200 with fresh data |

### 4.8 Caching Tests (Service)

**Question being answered:** "Does the cache behave correctly across reads and writes?"

| Scenario | Expected Behavior |
|----------|-------------------|
| First List call | Cache miss, data fetched from DB |
| Second identical List call | Cache hit, DB not queried |
| Create invalidates list cache | Next List call fetches from DB |
| Update invalidates entity cache | Next Get returns fresh data |
| Delete invalidates cache | Deleted entity not returned from cache |
| Different query params = different cache key | Separate cache entries |
| Cache disabled via feature flag | Always fetches from DB |
| Security active → cache bypassed | No cross-user data leakage |

### 4.9 HTTP Endpoint Tests (API)

**Question being answered:** "Does the HTTP API behave correctly per REST conventions?"

| Scenario | Expected Behavior |
|----------|-------------------|
| `GET /api/users` | 200, JSON array with pagination metadata |
| `GET /api/users/1` | 200, single JSON object |
| `GET /api/users/999` | 404 |
| `POST /api/users` with valid body | 201, `Location` header, created entity |
| `POST /api/users` with invalid body | 422 or 400, problem details with field errors |
| `PATCH /api/users/1` | 200, updated entity |
| `PATCH /api/users/999` | 404 |
| `DELETE /api/users/1` | 204 No Content |
| `DELETE /api/users/999` | 404 |
| `HEAD /api/users` | 200, `X-Total-Count` header, no body |
| `GET /api/users?filter[status]=active` | 200, only active users |
| `GET /api/users?sort=-createdAt` | 200, sorted descending |
| `GET /api/users?page=2&pageSize=10` | 200, correct page |
| `GET /api/users?search=alice` | 200, matching users |
| `GET /api/users?fields=id,name` | 200, only requested fields |
| `GET /api/users?expand=posts` | 200, nested posts included |
| `GET /api/$discovery` | 200, list of available resources |
| `GET /api/$schema/users` | 200, JSON Schema for users |
| Unsupported HTTP method | 405 Method Not Allowed |
| Malformed JSON body | 400 Bad Request |
| Content-Type not `application/json` | 415 Unsupported Media Type (or accepted) |

### 4.10 Dynamic Entity Tests (Service + API)

**Question being answered:** "Do runtime-defined entities work the same as static entities?"

| Scenario | Expected Behavior |
|----------|-------------------|
| Create a dynamic entity definition | Definition persisted |
| CRUD on dynamic entity via `/api/d/{route}` | Same behavior as static |
| Dynamic entity with validation | Validation enforced |
| Dynamic entity with expansion | Expansion works |
| Dynamic entity does not collide with static routes | Routing is correct |
| Update entity definition at runtime | New fields available immediately |
| Delete entity definition | CRUD endpoints stop working for it |

---

## 5. Scenario Matrix

For every CRUD operation, test across these dimensions:

```
                  ┌──────────────┐
                  │  Operation   │
                  │  (C/R/U/D/L) │
                  └──────┬───────┘
                         │
        ┌────────────────┼────────────────┐
        │                │                │
   ┌────▼────┐    ┌──────▼──────┐   ┌────▼────┐
   │  Input  │    │  Security   │   │  State  │
   │ Variant │    │  Context    │   │ Variant │
   └────┬────┘    └──────┬──────┘   └────┬────┘
        │                │               │
   - Valid           - Anonymous     - Empty table
   - Missing field   - Authenticated - Entity exists
   - Invalid type    - Wrong tenant  - Entity deleted
   - Extra fields    - No permission - Concurrent edit
   - Null values     - Admin role    - Cached
   - Boundary vals   - API key       - Expired cache
   - Empty strings                   - Related entities exist
   - Max length                      - No related entities
```

**For each combination that makes sense, there should be a test.** Not every combination is meaningful — use judgment — but the matrix ensures you don't forget an axis.

---

## 6. Test Writing Rules

### Rule 1: Name tests as behavior specifications

```
❌ Constructor_WithAllParameters_SetsAllProperties
❌ EnableRateLimiting_WhenSet_ReturnsSetValue

✅ Create_WithMissingRequiredField_Returns422WithFieldError
✅ List_WhenTenantIsolationEnabled_ReturnsOnlyCurrentTenantRecords
✅ Update_WithStaleETag_Returns412PreconditionFailed
✅ Get_WhenEntitySoftDeleted_Returns404
```

Format: **`{Action}_{Condition}_{ExpectedOutcome}`**

### Rule 2: Arrange-Act-Assert with clear separation

```csharp
[Fact]
public async Task Create_WithMissingRequiredField_ThrowsValidationException()
{
    // Arrange: set up a contract where "name" is RequiredOnCreate
    var service = CreateService(withEntity: typeof(User));
    var body = new JsonObject { ["email"] = "test@example.com" }; // no "name"

    // Act + Assert
    var act = () => service.CreateAsync("User", body);
    await act.Should().ThrowAsync<DataSurfaceValidationException>()
        .Where(e => e.Errors.Any(err => err.Field == "name"));
}
```

### Rule 3: One assertion per test (or one logical assertion)

A test that asserts 15 different properties is really 15 tests crammed into one. When it fails, you don't know which behavior broke. Exceptions: asserting multiple fields of a single response object is acceptable as one logical assertion ("the response shape is correct").

### Rule 4: Tests must not depend on execution order

Every test must create its own state. No shared mutable state. Use fresh `DbContext` per test (or per class with fixture).

### Rule 5: Use test data builders, not raw constructors

```csharp
// Bad — brittle, 16 positional parameters
var contract = new FieldContract("Name", "name", FieldType.String, false,
    true, true, true, true, false, false, false, false, false, null, null,
    new FieldValidationContract(false, null, null, null, null, null));

// Good — readable, only specify what matters for this test
var contract = A.Field("name")
    .OfType(FieldType.String)
    .Filterable()
    .RequiredOnCreate()
    .Build();
```

### Rule 6: Mock at the boundary, not in the middle

- Mock `IWebhookPublisher` (external side effect)
- Mock `IAuditLogger` (external side effect)
- Mock `ITenantResolver` (external dependency)
- Do NOT mock `EfCrudQueryEngine` or `EfCrudMapper` — they are core logic we want to test

### Rule 7: Every bug gets a regression test FIRST

When a bug is found:
1. Write a failing test that reproduces the bug
2. Verify the test fails
3. Fix the bug
4. Verify the test passes

---

## 7. What NOT to Test

Deleting bad tests is as important as writing good ones. Remove or don't write tests for:

| Skip this | Why |
|-----------|-----|
| Record/POCO property getters and setters | Tests C# language features, not your code |
| `enum.Should().Be(enum)` | Tautology |
| Default values of options classes | Only useful if the default is load-bearing (e.g., `MaxPageSize` default matters, `EnableWebhooks` default doesn't) |
| Internal implementation details | Refactoring-proof tests don't break when you reorganize code |
| Frameworks (EF Core, ASP.NET) | Trust Microsoft's tests; only test YOUR usage of the framework |

### Existing tests to reclassify or delete

| Current Test | Verdict |
|---|---|
| `CrudFieldAttributeTests` (all 13) | **Delete** — tests property getters on a POCO. Zero value. |
| `CrudTenantAttributeTests` (all 5) | **Delete** — same. Attribute defaults are trivially tested through contract builder tests. |
| `FieldContractTests` (all 4) | **Delete** — tests `record` constructor. C# guarantees this. |
| `FieldValidationContractTests` (all 4) | **Delete** — same. |
| `QueryContractTests` (all 4) | **Delete** — same. |
| `ImportExportTests` (most) | **Delete most** — `ImportOptions` default test is marginal. `ExportFormat.Json.Should().Be(ExportFormat.Json)` is a tautology. |
| `WebhookEventTests` (all 6) | **Delete** — tests `record` constructor. |
| `DataSurfaceHttpOptionsTests` (all 13) | **Delete** — tests property getters. Only keep if a default value is security-sensitive. |
| `QuerySpecTests` (7) | **Keep 2** — the `with` expression tests have some value. Delete the rest. |
| `QueryEngineTests` (17) | **Keep all** — these test real behavior. Expand them. |
| `ContractBuilderIntegrationTests` (8) | **Keep all** — these test real behavior. Expand them. |
| `QueryEngineIntegrationTests` (11) | **Keep all** — these test real behavior. Expand them. |

**Net: ~48 tests deleted, ~38 kept, 200+ new tests to write.**

---

## 8. Execution Plan

### Phase 1: Foundation (do this first)

1. **Create shared test infrastructure**
   - Test data builders (`A.Field(...)`, `A.Contract(...)`, `A.Entity(...)`)
   - `TestServiceFactory` — spins up real services with SQLite in-memory
   - `TestApiFactory` — `WebApplicationFactory<Program>` with seeded data
   - Shared `TestDbContext` with all entity types

2. **Write contract builder behavioral tests** (~30 tests)
   - Replaces the deleted attribute/record tests
   - Tests the actual pipeline: attributes → `ContractBuilder` → `ResourceContract`
   - Covers all annotation combinations and edge cases from §4.1

3. **Expand query engine tests** (~20 new tests)
   - Edge cases from §4.2 (bad input, boundary values, special characters)

### Phase 2: Core CRUD (most critical)

4. **Write CRUD service tests** (~40 tests)
   - All scenarios from §4.4
   - Uses real `EfDataSurfaceCrudService` with SQLite in-memory
   - Mocks only external dependencies (`IWebhookPublisher`, `IAuditLogger`)

5. **Write validation tests** (~25 tests)
   - All scenarios from §4.3
   - Tests the full path: JSON body → validation → accept/reject

### Phase 3: Security & Middleware

6. **Write security tests** (~25 tests)
   - All scenarios from §4.6
   - Uses mock `ITenantResolver`, `IResourceFilter`, `IFieldAuthorizer`

7. **Write hook tests** (~15 tests)
   - All scenarios from §4.5
   - Uses concrete hook implementations that record their calls

8. **Write concurrency tests** (~10 tests)
   - All scenarios from §4.7

9. **Write caching tests** (~12 tests)
   - All scenarios from §4.8

### Phase 4: HTTP Layer

10. **Write API endpoint tests** (~40 tests)
    - All scenarios from §4.9
    - Full HTTP requests via `WebApplicationFactory`
    - Verifies status codes, headers, response shapes

### Phase 5: Dynamic & Advanced

11. **Write dynamic entity tests** (~15 tests)
    - All scenarios from §4.10

12. **Write bulk/streaming/import-export tests** (~15 tests)

### Estimated total: **~250 behavioral tests**

---

## 9. CI Integration

### Pipeline structure

```yaml
name: Build & Test

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - run: dotnet restore
      - run: dotnet build -c Release --no-restore

      # Unit tests (fast, run first)
      - run: dotnet test DataSurface.Tests.Unit -c Release --no-build --logger trx

      # Service tests
      - run: dotnet test DataSurface.Tests.Service -c Release --no-build --logger trx

      # API tests
      - run: dotnet test DataSurface.Tests.Api -c Release --no-build --logger trx

      # Coverage (only meaningful once tests are behavioral)
      - run: dotnet test -c Release --no-build --collect:"XPlat Code Coverage"
      - uses: codecov/codecov-action@v4
```

### Quality gates

| Metric | Threshold | Why |
|--------|-----------|-----|
| All tests pass | Required | No regressions |
| Coverage ≥ 70% | Recommended (not blocking) | Coverage is a secondary signal after test quality |
| No test duration > 5s | Warning | Keeps suite fast |
| Zero skipped tests | Warning | Skipped tests are hidden debt |

### Coverage philosophy

**Coverage is only meaningful when tests are behavioral.** A test suite with 95% coverage that only tests property getters is worse than one with 60% coverage that tests every CRUD operation end-to-end. We will track coverage after Phase 2 is complete, not before.

---

## Summary

| Problem | Solution |
|---------|----------|
| Tests validate implementation | Write tests from the README/docs, not from the code |
| Partial scenario coverage | Use the scenario matrix (§5) as a checklist |
| Coverage is misleading | Delete hollow tests, replace with behavioral ones |
| No negative tests | Pair every happy-path test with at least one failure test |
| No HTTP-level tests | Add `WebApplicationFactory` test layer |
| No security tests | Dedicated security test category with mock resolvers |
| No concurrency tests | ETag/If-Match test scenarios |
| Test data is fragile | Fluent test data builders |
