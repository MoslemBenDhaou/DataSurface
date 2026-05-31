# Roadmap

> Features and improvements under consideration for **future releases**.

This document lists work that is **not yet implemented**. It is intentionally **not** tied to specific versions or dates — items are grouped by area, and ordering does not imply priority or commitment. Anything already shipped is documented in the [main documentation](index.md); if a capability appears both here and in a feature guide, the feature guide is authoritative.

Have a request? Open an issue with the `enhancement` label.

---

## API & Protocol

- **GraphQL endpoint** — auto-generate a schema from `ResourceContract` definitions (queries + mutations), reusing the existing validation/security/hooks pipeline.
- **gRPC service** — generate `.proto` from contracts and serve gRPC alongside REST.
- **OData query compatibility** — accept `$filter`, `$select`, `$expand`, `$orderby`, `$top`, `$skip` as an alternative query syntax mapped onto `QuerySpec`.
- **JSON Patch (RFC 6902)** — accept `application/json-patch+json` on PATCH, validated against the contract.
- **Conditional creates** — `If-None-Match: *` on POST for idempotent/retry-safe creation, returning `412` on conflict.
- **Batch / multi-resource requests** — a single `POST /api/$batch` grouping operations across resources with all-or-nothing semantics.
- **HATEOAS / hypermedia links** — optional `_links` (self, next, prev, related) for discoverability.
- **API versioning** — versioned routes (`/api/v1/...`), version-to-contract mapping, deprecation markers.

## Querying & Data Access

- **Cursor-based (keyset) pagination** — `?cursor=` with `nextCursor`/`prevCursor`, more efficient than offset for large datasets.
- **COUNT endpoint** — `GET /api/{resource}/$count` returning a plain integer.
- **Aggregation queries** — `$aggregate` with server-side `GROUP BY` / `SUM` / `AVG` / `MIN` / `MAX` / `COUNT`.
- **Full-text search for dynamic entities** — `?q=` search currently applies to EF/static resources only; extend it to the dynamic backend.

## Developer Experience

- **Fluent configuration API** — `builder.Resource<T>().Field(x => x.Name).Required().MaxLength(200)` as an alternative to attributes.
- **Single-call registration** — a unified `AddDataSurface()` that wires all services, reducing manual registration boilerplate.
- **Expanded Roslyn analyzers & code fixes** — diagnostics for missing `[CrudKey]`, invalid `[CrudField]` flag combinations, with quick-fixes.
- **Typed client SDK generation** — strongly-typed C# HTTP clients (and optional TypeScript types) generated from contracts.
- **`dotnet new` templates** — `datasurface-api` and `datasurface-entity` scaffolds.
- **Hot-reload for dynamic contracts** — detect `EntityDef`/`PropertyDef` changes and refresh routes/contracts without restart.
- **Pipeline visualization** — a diagnostic endpoint listing the active hooks, filters, authorizers, and cache layers for a resource.
- **Contract diff / changelog** — structured diff between contract versions with breaking-change detection for CI gates.

## Security & Compliance

- **Per-resource CORS** — CORS policies scoped to a resource/operation (today CORS is left entirely to the host).
- **OAuth2 / OIDC scope mapping** — map CRUD operations to scopes (`users:read`, `users:write`) validated from JWT claims.
- **Field-level encryption** — `[CrudField(Encrypted = true)]` with a pluggable `IFieldEncryptor`.
- **Field masking / PII redaction** — `[CrudSensitive]` with role-based masking, building on `IFieldAuthorizer`.
- **IP allow/deny lists** — per-resource or global IP restrictions.
- **Request signing (HMAC)** — signature-based machine-to-machine authentication.
- **Audit log retention & archival** — configurable purge/archive to bound audit-table growth.

## Performance & Scalability

- **Response compression** — Brotli/gzip for large list responses and exports.
- **Query cost analysis** — estimate complexity and reject queries exceeding configurable limits.
- **Read replicas / multi-database routing** — route reads to replicas, or different resources to different databases.
- **Background write queue** — optional async writes returning `202 Accepted` with a job id.
- **DbContext pooling guidance** — first-class support for `AddDbContextPool` plus a pool-saturation health check.

## Data Management

- **Unique constraint validation** — `[CrudField(Unique = true)]` / `[CrudUnique(...)]` with composite support and a pre-insert `409` (today duplicates are only caught reactively from the database error).
- **CSV import** — the import endpoint accepts JSON arrays only; add CSV (export already supports CSV).
- **Default values & computed fields for dynamic entities** — these apply to EF/static resources today; extend them to the dynamic backend.
- **Cross-backend expansion** — expand relations across the dynamic ↔ EF Core boundary (currently same-backend only).
- **Soft-delete recovery** — `POST /{id}/restore` and `?includeDeleted=true` (admin), building on `ISoftDelete`.
- **Change data capture / history** — entity versioning, `GET /{id}/history`, and temporal `?asOf=` queries.
- **Cascade delete rules** — `[CrudRelation(OnDelete = ...)]` (`Cascade`, `SetNull`, `Restrict`, `NoAction`).
- **Schema migrations from contracts** — generate EF Core migrations from contract changes, with a CLI command.
- **Data seeding via contracts** — declarative seed data or an `ISeedDataProvider`.
- **Archival / lifecycle policies** — auto-archive aged records to cold storage.
- **Optimistic offline sync** — conflict-resolution strategies and client version vectors.

## Observability & Diagnostics

- **Slow query logging** — threshold-based logging including the generated SQL.
- **Request/response logging middleware** — full payload logging with sensitive-field redaction.
- **Custom metric tags** — user-defined dimensions (tenant, region, …) on OpenTelemetry metrics.
- **Error-rate alerting** — emit threshold events via `IWebhookPublisher`.
- **OpenTelemetry logs exporter** — OTLP-native log/trace/metric correlation.

## Integrations & Ecosystem

- **Webhooks for dynamic entities** — mutation webhooks fire for EF/static resources today; extend to the dynamic service.
- **Webhook management endpoints** — admin CRUD for `WebhookSubscription` plus a test-send action.
- **Event bus integration** — publish domain events via MassTransit / MediatR after CRUD operations.
- **Message queue publishing** — RabbitMQ / Azure Service Bus / Kafka after mutations.
- **NoSQL backends** — MongoDB / Cosmos DB via the same contract + HTTP pipeline.
- **Dapper / raw-SQL backend** — a lightweight read-heavy alternative to EF Core.
- **File / blob attachments** — `FieldType.File` with `IFileStorage` and pre-signed URLs.
- **Notification integration** — email / SMS / push on CRUD events via `INotificationSender`.

## Admin & Tooling

- **Admin web dashboard** — manage dynamic entity definitions, browse contracts, run test queries, view audit logs and metrics.
- **CLI tool (`dotnet-datasurface`)** — `list`, `validate`, `export-schema`, `scaffold`.
- **Dynamic entity migration tooling** — DDL generation and rollback for definition changes.
- **API playground** — an interactive, contract-aware explorer in the admin panel.

## Testing & Project Infrastructure

- **In-memory test server helpers** — `WebApplicationFactory`-based harness for integration tests.
- **Contract snapshot testing** — JSON snapshots with CI verification of unintended changes.
- **Fuzz testing** — random filter/sort/expand strings against the query parser.
- **Load / performance suite** — end-to-end HTTP benchmarks with CI regression tracking.
- **Mutation testing** — measure test effectiveness on validation, security, and the query engine.
- **CI pipeline** — PR build + test, code coverage, and benchmark-regression checks.

## Documentation & Samples

- **Sample projects** — minimal API, multi-tenant SaaS, dynamic CMS, and event-driven microservice.
- **Migration guide** — moving from traditional controllers to DataSurface, with before/after.
- **Performance tuning guide** — caching, indexing aligned to filterable/sortable fields, benchmark interpretation.
- **Security hardening guide** — production checklist and OWASP alignment.
- **Architecture Decision Records** — key design rationale under `docs/adr/`.
