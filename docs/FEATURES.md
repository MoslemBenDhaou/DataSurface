# Proposed Features for DataSurface

> A prioritized list of features, improvements, and enhancements identified through a deep scan of the codebase, architecture, documentation, existing planned features, test coverage, CI/CD, and developer experience.

---

## Table of Contents

- [1. API & Protocol Enhancements](#1-api--protocol-enhancements)
- [2. Developer Experience (DX)](#2-developer-experience-dx)
- [3. Security & Compliance](#3-security--compliance)
- [4. Performance & Scalability](#4-performance--scalability)
- [5. Data Management](#5-data-management)
- [6. Observability & Diagnostics](#6-observability--diagnostics)
- [7. Testing & Quality](#7-testing--quality)
- [8. Ecosystem & Integrations](#8-ecosystem--integrations)
- [9. Admin & Tooling](#9-admin--tooling)
- [10. Documentation & Samples](#10-documentation--samples)

---

## 1. API & Protocol Enhancements

### 1.1 GraphQL Endpoint *(already planned — high priority)*
- Auto-generate a `/api/graphql` schema from `ResourceContract` definitions
- Support queries, mutations, and field-level selection natively via GraphQL
- Leverage existing contracts for authorization, validation, and hooks

### 1.2 gRPC Support *(already planned — medium priority)*
- Generate `.proto` definitions from contracts
- Offer gRPC endpoints alongside REST for high-performance internal communication
- Share the same hook/validation/security pipeline

### 1.3 Real-time Updates via SignalR / WebSocket *(already planned — medium priority)*
- Push live change notifications when CRUD operations occur
- Per-resource subscription channels (e.g., `subscribe:users`)
- Integrate with existing webhook infrastructure for event sourcing

### 1.4 OData Query Compatibility *(already planned — lower priority)*
- Support `$filter`, `$select`, `$expand`, `$orderby`, `$top`, `$skip` as alternative query syntax
- Map OData expressions to existing `QuerySpec` / `ExpandSpec` internally
- Optional — enabled via a feature flag or separate middleware

### 1.5 JSON Patch (RFC 6902) Support *(already planned — lower priority)*
- Accept `application/json-patch+json` content type on PATCH endpoints
- Parse RFC 6902 operations (`add`, `remove`, `replace`, `move`, `copy`, `test`)
- Validate patch operations against the `ResourceContract`

### 1.6 Conditional Creates (Idempotent POST)
- Support `If-None-Match: *` header on POST to prevent duplicate creation
- Return `412 Precondition Failed` if the resource already exists
- Useful for retry-safe integrations and mobile/offline clients

### 1.7 Cursor-Based Pagination
- Add `?cursor=<opaque_token>` as an alternative to offset-based pagination
- More efficient for large datasets and real-time feeds
- Return `nextCursor` / `prevCursor` in response body and `Link` headers

### 1.8 HATEOAS / Hypermedia Links
- Optionally include `_links` in responses (self, next, prev, related resources)
- Enable via `DataSurfaceHttpOptions.EnableHypermediaLinks`
- Improves API discoverability and REST maturity (Level 3)

### 1.9 Batch/Multi-Resource Requests
- Support a single POST to `/api/$batch` that groups multiple operations across different resources
- Execute in a single transaction with all-or-nothing semantics
- Useful for complex frontend workflows needing atomicity

### 1.10 COUNT Endpoint
- Dedicated `GET /api/{resource}/$count?filter[…]=…` returning a plain integer
- Lighter than HEAD (no need to parse response headers)
- Useful for dashboards and aggregation UI

### 1.11 Aggregation Queries
- Support `GET /api/{resource}/$aggregate?group=status&sum=amount&count=true`
- Server-side `GROUP BY`, `SUM`, `AVG`, `MIN`, `MAX`, `COUNT`
- Return aggregated results without loading individual records

---

## 2. Developer Experience (DX)

### 2.1 Fluent Configuration API *(already planned — high priority)*
- `builder.Resource<T>().Field(x => x.Name).Required().MaxLength(200)` syntax
- Alternative to attribute-based configuration for teams preferring code-over-attributes
- Support both approaches side by side

### 2.2 Unified `AddDataSurface()` Registration Extension
- Single `builder.Services.AddDataSurface(opt => { ... })` call that registers all services
- Currently users must manually register `CrudHookDispatcher`, `CrudOverrideRegistry`, `EfDataSurfaceCrudService`, and `IDataSurfaceCrudService`
- Reduce boilerplate and potential misconfiguration

### 2.3 Typed Client SDK / Code Generation
- Generate strongly-typed C# HTTP clients from contracts (e.g., `IUserClient.GetAsync(id)`)
- Optionally produce TypeScript client types for frontend consumption
- Leverage the source generator (`DataSurface.Generator`) infrastructure

### 2.4 `dotnet new` Project Templates
- `dotnet new datasurface-api` — scaffold a working project with entities, contracts, and endpoints
- `dotnet new datasurface-entity` — scaffold a single entity with attributes
- Lower the barrier to entry for new users

### 2.5 Roslyn Analyzers & Code Fixes
- Warn when `[CrudResource]` class has no `[CrudKey]` property
- Warn when `[CrudField]` has `RequiredOnCreate = true` but no `CrudDto.Create` flag
- Quick-fix to auto-add missing attributes
- Expand existing `Diagnostics.cs` in `DataSurface.Generator`

### 2.6 Hot-Reload for Dynamic Contracts
- Automatically detect changes to `EntityDef` / `PropertyDef` in the database
- Refresh routes and contracts at runtime without app restart
- Publish a `ContractChanged` event for cache invalidation

### 2.7 Middleware Pipeline Visualization
- Diagnostic endpoint (e.g., `/api/$pipeline/{resource}`) showing the active middleware chain
- Lists active hooks, security filters, authorizers, cache layers, and webhook publishers
- Useful for debugging configuration issues

### 2.8 Contract Diff / Changelog
- Compare two versions of a contract and produce a structured diff
- Detect breaking changes (removed fields, narrowed validation, disabled operations)
- Useful for CI gates and API versioning

---

## 3. Security & Compliance

### 3.1 Field Masking / PII Redaction *(already planned — lower priority)*
- `[CrudField(Masked = true)]` or `[CrudSensitive]` attribute
- Automatically mask sensitive fields (e.g., `"email": "j***@example.com"`) based on caller role
- Integrate with `IFieldAuthorizer` for conditional masking

### 3.2 IP Allowlisting / Denylisting
- Configure per-resource or global IP restrictions
- `DataSurfaceHttpOptions.AllowedIpRanges` / `DeniedIpRanges`
- Complement existing API key and rate limiting features

### 3.3 Request Signing / HMAC Verification
- Verify inbound requests with HMAC signatures (for machine-to-machine calls)
- Extend `IApiKeyValidator` to support signature-based auth
- Pair with webhook secret verification for bidirectional trust

### 3.4 CORS Configuration per Resource
- Allow CORS policies per resource or operation via `[CrudAuthorize(CorsPolicy = "...")]`
- Currently no per-resource CORS support — everything relies on global ASP.NET Core CORS

### 3.5 Data Encryption at Field Level
- Support transparent encryption/decryption of specific fields (e.g., SSN, credit card)
- `[CrudField(Encrypted = true)]` with a pluggable `IFieldEncryptor` interface
- Data stored encrypted in the database, decrypted on read for authorized users

### 3.6 Audit Log Retention & Archival
- Auto-purge or archive audit logs older than a configurable duration
- `DataSurfaceAuditOptions.RetentionDays` / `ArchiveToStorage`
- Prevent unbounded growth of audit tables

### 3.7 OAuth2 / OpenID Connect Scopes Mapping
- Map CRUD operations to OAuth2 scopes (e.g., `users:read`, `users:write`)
- Auto-validate scopes from JWT claims against contract security policies
- Simplify integration with identity providers like Auth0, Entra ID, Keycloak

---

## 4. Performance & Scalability

### 4.1 Compiled Query Integration *(already identified — high priority)*
- `CompiledQueryCache` exists but is not used by `EfDataSurfaceCrudService`
- Integrate compiled queries for `FindByIdAsync` and common list patterns
- Significant performance improvement for high-throughput scenarios

### 4.2 Query Cost Analysis *(already planned — lower priority)*
- Estimate query complexity before execution (number of joins, filter depth, expansion depth)
- Reject or warn on expensive queries exceeding configurable thresholds
- `DataSurfaceHttpOptions.MaxQueryCost`

### 4.3 Read Replicas / Multi-Database Routing *(already planned — lower priority)*
- Route read operations to replica databases, writes to primary
- `[CrudResource(ReadConnectionName = "replica")]`
- Support different databases per resource

### 4.4 Response Compression
- Enable Brotli/Gzip compression for large list responses and streaming endpoints
- Configurable per resource or globally
- Especially impactful for CSV/JSON export endpoints

### 4.5 Background Write Queue
- Optionally queue write operations (Create, Update, Delete) for async processing
- Return `202 Accepted` with a job ID
- Pair with the planned Async Job Queue feature for status tracking

### 4.6 Connection Pooling Optimization
- Provide guidance/configuration for DbContext pooling (`AddDbContextPool`)
- Ensure DataSurface services work correctly with pooled contexts
- Add health check for connection pool saturation

### 4.7 Partial Response Caching
- Cache individual entity responses (GET by ID) with cache key = `{resource}:{id}:{etag}`
- Invalidate on update/delete
- Currently only list results are cached via `IQueryResultCache`

---

## 5. Data Management

### 5.1 Change Data Capture (CDC) *(already planned — high priority)*
- Track historical changes with entity versioning
- `GET /api/users/{id}/history` — retrieve change log for a specific entity
- Support temporal queries (`?asOf=2024-01-01T00:00:00Z`)

### 5.2 Soft-Delete Recovery
- `POST /api/users/{id}/restore` — restore soft-deleted entities
- `GET /api/users?includeDeleted=true` — optionally include deleted records (admin only)
- Extend existing `ISoftDelete` infrastructure

### 5.3 Schema Migrations *(already planned — lower priority)*
- Track contract changes and generate EF Core migrations automatically
- Detect new fields, removed fields, type changes, validation changes
- `dotnet datasurface migrate` CLI command

### 5.4 Data Seeding via Contracts
- Define seed data alongside resource contracts
- `[CrudResource("roles", SeedData = "admin,user,viewer")]`
- Or a separate `ISeedDataProvider` interface for complex seeding

### 5.5 Archival / Data Lifecycle Management
- Auto-archive records older than a configurable threshold
- `[CrudResource(ArchiveAfterDays = 365)]`
- Move to cold storage or a separate archive table

### 5.6 Cross-Backend Expansion *(already planned — medium priority)*
- Expand dynamic entities referencing EF entities and vice versa
- Currently expansion only works within the same backend
- Requires coordination between `EfDataSurfaceCrudService` and `DynamicDataSurfaceCrudService`

### 5.7 Cascade Delete Rules
- Configure cascade behavior via contract: `[CrudRelation(OnDelete = CascadeMode.SetNull)]`
- Options: `Cascade`, `SetNull`, `Restrict`, `NoAction`
- Currently relies on EF Core/database-level configuration only

### 5.8 Unique Constraint Validation
- `[CrudField(Unique = true)]` or `[CrudUnique("email")]` at entity level
- Check for uniqueness before insert and return 409 Conflict
- Support composite unique constraints: `[CrudUnique("tenantId", "email")]`

### 5.9 Optimistic Offline Sync *(already planned — lower priority)*
- Conflict resolution strategies for mobile/offline scenarios
- Merge, last-write-wins, or custom conflict resolver
- Track client version vectors alongside server row versions

---

## 6. Observability & Diagnostics

### 6.1 Dashboard / Metrics UI
- Built-in admin page showing live operation counts, latencies, error rates
- Per-resource breakdown with charts
- Integrate with existing `DataSurfaceMetrics`

### 6.2 Slow Query Logging
- Log queries exceeding a configurable threshold (e.g., 500ms)
- Include the generated SQL, filter parameters, and execution plan hints
- `DataSurfaceEfCoreOptions.SlowQueryThresholdMs`

### 6.3 Request/Response Logging Middleware
- Optional middleware to log full request bodies and response payloads
- Configurable per resource and operation
- Sensitive field redaction via `IFieldAuthorizer`

### 6.4 Custom Metric Tags
- Allow users to inject custom tags/labels into OpenTelemetry metrics
- e.g., `tenant_id`, `region`, `api_version`
- `DataSurfaceMetrics.AddCustomTag(name, valueResolver)`

### 6.5 Error Rate Alerting Integration
- Emit structured events when error rate exceeds a threshold
- Integrate with `IWebhookPublisher` to push alerts
- Configurable per resource

### 6.6 OpenTelemetry Logs Exporter
- Integrate structured logs with OpenTelemetry log exporters
- Correlation between logs, traces, and metrics using trace context
- Currently uses standard `ILogger` — add OTLP-native correlation

---

## 7. Testing & Quality

### 7.1 In-Memory Test Server / `WebApplicationFactory` Helpers
- `DataSurfaceTestServer.Create<TContext>()` — spin up a full in-memory test server
- Pre-configured with SQLite in-memory for integration tests
- Reduce boilerplate in `DataSurface.Tests.Integration`

### 7.2 Contract Snapshot Testing
- Serialize contracts to JSON and compare against committed snapshots
- Detect unintended contract changes in CI
- `dotnet datasurface snapshot --verify`

### 7.3 Fuzz Testing for Query Parser
- Generate random filter/sort/expand strings and verify no unhandled exceptions
- Target `DataSurfaceQueryParser` and `EfCrudQueryEngine`
- Use property-based testing (e.g., FsCheck or similar)

### 7.4 Load / Performance Test Suite
- Extend `DataSurface.Benchmarks` with end-to-end HTTP benchmarks
- Measure throughput under concurrent load for CRUD operations
- Track regressions in CI via benchmark comparison

### 7.5 Test Data Builders
- Fluent API for generating test entities: `TestData.User().WithEmail("x@y.com").Build()`
- Aligned with contract definitions for consistency
- Useful for both unit and integration tests

### 7.6 Mutation Testing
- Integrate mutation testing (e.g., Stryker.NET) to measure test effectiveness
- Focus on critical paths: validation, security dispatching, query engine
- Report mutation score in CI

### 7.7 CI Pipeline Enhancements
- Currently only `publish-nuget.yml` exists — add:
  - **Build & test** workflow on every PR
  - **Code coverage** reporting (e.g., Codecov / Coveralls)
  - **Benchmark regression** checks on main branch
  - **NuGet package validation** (API compatibility checks)

---

## 8. Ecosystem & Integrations

### 8.1 Event Bus Integration (MassTransit / MediatR)
- Publish domain events after CRUD operations via MassTransit, MediatR, or a custom `IEventBus`
- More robust than webhooks for internal microservice communication
- `builder.Services.AddDataSurfaceEvents<MassTransitPublisher>()`

### 8.2 Dapper / Raw SQL Fallback Backend
- `StorageBackend.Dapper` for scenarios where EF Core overhead is undesirable
- Direct SQL queries using the same `ResourceContract` definitions
- Useful for read-heavy microservices or legacy databases

### 8.3 NoSQL Backend Support (MongoDB / Cosmos DB)
- `StorageBackend.MongoDb` or `StorageBackend.CosmosDb`
- Map `ResourceContract` to document collections
- Reuse the same HTTP layer, hooks, and security pipeline

### 8.4 Messaging Queue Integration
- Automatically publish to RabbitMQ / Azure Service Bus / Kafka after mutations
- Configurable per resource: `[CrudResource(PublishTo = "user-events")]`
- Complement to webhooks for durable messaging

### 8.5 File/Blob Attachment Support
- `[CrudField(Type = FieldType.File)]` — support file upload fields
- Store in Azure Blob Storage, S3, or local filesystem via `IFileStorage`
- Return pre-signed URLs for download

### 8.6 Notification Integration
- Send email / SMS / push notifications on CRUD events
- Pluggable `INotificationSender` interface
- Template-based notifications keyed by resource + operation

### 8.7 API Versioning Support
- Support versioned endpoints: `/api/v1/users`, `/api/v2/users`
- Map different contract versions to different API versions
- Backward-compatible field additions, deprecation markers on old fields

---

## 9. Admin & Tooling

### 9.1 Admin UI (Web Dashboard)
- Blazor or React-based admin dashboard for:
  - Browsing / editing dynamic entity definitions
  - Viewing registered contracts and their fields
  - Running test queries against resources
  - Viewing audit logs and metrics
- Ship as `DataSurface.Admin.UI` NuGet package

### 9.2 CLI Tool (`dotnet-datasurface`)
- `dotnet datasurface list` — list all registered contracts
- `dotnet datasurface validate` — verify contract consistency
- `dotnet datasurface export-schema` — export OpenAPI / JSON Schema
- `dotnet datasurface scaffold` — generate entity boilerplate from database

### 9.3 Dynamic Entity Migration Tooling
- Track changes to dynamic entity definitions over time
- Produce DDL statements for dynamic table schema changes
- Rollback support for dynamic entity changes

### 9.4 API Playground / Try-It Console
- Interactive API explorer embedded in the admin panel
- Similar to Swagger UI but tailored for DataSurface features (filters, expand, fields)
- Pre-populate with contract metadata for auto-complete

### 9.5 Webhook Management Endpoints
- `GET /admin/ds/webhooks` — list webhook subscriptions
- `POST /admin/ds/webhooks` — create a new subscription
- `POST /admin/ds/webhooks/{id}/test` — send a test event
- Currently `WebhookSubscription` record exists but has no CRUD management

---

## 10. Documentation & Samples

### 10.1 Sample Projects
- **Minimal API** — simplest possible setup with 1-2 entities
- **Multi-tenant SaaS** — demonstrates tenant isolation, row-level security
- **Dynamic CMS** — runtime-defined content types via Admin endpoints
- **Microservice** — event-driven architecture with webhooks + message bus

### 10.2 Migration Guide
- Step-by-step guide for migrating from traditional controllers to DataSurface
- Before/after comparisons for common patterns
- Handling edge cases (custom validation, complex business logic)

### 10.3 Performance Tuning Guide
- Best practices for query caching, compiled queries, pagination
- Database indexing recommendations aligned with filterable/sortable fields
- Benchmark interpretation guide (expand on existing `Benchmarks.md`)

### 10.4 Security Hardening Guide
- Comprehensive checklist for production deployments
- Rate limiting, API key rotation, tenant isolation, field authorization
- OWASP alignment for common API vulnerabilities

### 10.5 Architecture Decision Records (ADRs)
- Document key architectural decisions (contract-driven design, backend abstraction, etc.)
- Useful for contributors and enterprise adopters
- Store in `/docs/adr/` directory

### 10.6 Interactive API Documentation
- Enhance OpenAPI output with examples, descriptions, and markdown
- Auto-generate request/response examples from contracts
- Consider Redoc or Scalar for a polished docs experience

---

## Priority Summary

| Priority | Features |
|----------|----------|
| **High** | Unified registration (2.2), Compiled query integration (4.1), CI pipeline (7.7), Fluent config (2.1), GraphQL (1.1), CDC (5.1), Sample projects (10.1) |
| **Medium** | Cursor pagination (1.7), Soft-delete recovery (5.2), Admin UI (9.1), gRPC (1.2), Real-time (1.3), Event bus (8.1), Unique constraints (5.8), Test server helpers (7.1) |
| **Lower** | OData (1.4), JSON Patch (1.5), Field masking (3.1), Query cost (4.2), API versioning (8.7), CLI tool (9.2), NoSQL backends (8.3), Schema migrations (5.3) |
| **Nice to have** | HATEOAS (1.8), Batch requests (1.9), Aggregation (1.11), IP allowlisting (3.2), Fuzz testing (7.3), Mutation testing (7.6), File attachments (8.5) |
