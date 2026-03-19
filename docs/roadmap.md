# Roadmap

> Track every feature across releases. Each item has a unique ID that maps to [FEATURES.md](../FEATURES.md) where applicable.

```
v1.0.0 ████████████████████████████████████████ CURRENT
v1.1.0 ░░░░░░░░░░░░░░░░░░░░                    NEXT
v1.2.0 ░░░░░░░░░░░░░░░░                         PLANNED
v1.3.0 ░░░░░░░░░░░░                              PLANNED
v2.0.0 ░░░░░░░░                                   FUTURE
```

#### How to read this document

- **Shipped** — released, stable, tested
- **In Progress** — actively being worked on
- **Planned** — committed to this version
- **Exploring** — under consideration, not committed

---

## v1.0.0 — Foundation `CURRENT`

*The contract-driven CRUD model, query engine, security, extensibility, dynamic entities, and observability — all from day one.*

<details>
<summary><strong>55 features shipped</strong> — click to expand</summary>

#### Core CRUD

| # | Feature | Status |
|---|---------|--------|
| — | CRUD endpoints (List, Get, Create, Update, Delete, HEAD) | Shipped |
| — | PATCH partial update | Shipped |
| — | PUT full replacement (opt-in) | Shipped |
| — | In-process `IDataSurfaceCrudService` | Shipped |
| — | Soft delete via `ISoftDelete` | Shipped |
| — | Auto-timestamps via `ITimestamped` | Shipped |

#### Contract System

| # | Feature | Status |
|---|---------|--------|
| — | `ResourceContract` full schema | Shipped |
| — | Attribute-based `ContractBuilder` (assembly scanning) | Shipped |
| — | DB-metadata `DynamicContractBuilder` | Shipped |
| — | `CompositeResourceContractProvider` (static + dynamic merge) | Shipped |
| — | Startup contract validation (fail-fast diagnostics) | Shipped |
| — | Schema endpoint `GET /api/$schema/{route}` | Shipped |
| — | Resource discovery `GET /api/$resources` | Shipped |

#### Querying

| # | Feature | Status |
|---|---------|--------|
| — | Pagination (page, pageSize, X-Total-Count) | Shipped |
| — | Filters: eq, neq, gt, gte, lt, lte, contains, starts, ends, in, isnull | Shipped |
| — | Sorting (single + multi-field, default sort) | Shipped |
| — | Full-text search `?q=` across searchable fields | Shipped |
| — | Relation expansion `?expand=` with depth limit | Shipped |
| — | Filter / sort allowlists (contract-driven) | Shipped |

#### Security

| # | Feature | Status |
|---|---------|--------|
| — | Authorization policies via `[CrudAuthorize]` | Shipped |
| — | Row-level security via `IResourceFilter<T>` | Shipped |
| — | Resource-level authorization via `IResourceAuthorizer<T>` | Shipped |
| — | Field-level authorization via `IFieldAuthorizer` | Shipped |

#### Concurrency

| # | Feature | Status |
|---|---------|--------|
| — | ETag generation from row version | Shipped |
| — | `If-Match` optimistic concurrency (409 Conflict) | Shipped |
| — | Conditional GET `If-None-Match` (304 Not Modified) | Shipped |
| — | Row version convention (auto-configured `byte[]`) | Shipped |

#### Extensibility

| # | Feature | Status |
|---|---------|--------|
| — | Global hooks `ICrudHook` | Shipped |
| — | Typed hooks `ICrudHook<T>` | Shipped |
| — | Dynamic resource hooks `ICrudHookResource` | Shipped |
| — | Operation overrides `CrudOverrideRegistry` | Shipped |

#### Dynamic Entities

| # | Feature | Status |
|---|---------|--------|
| — | `EntityDef` / `PropertyDef` metadata model | Shipped |
| — | DynamicJson, DynamicEav, DynamicHybrid backends | Shipped |
| — | Admin API (CRUD for entity definitions) | Shipped |
| — | Definition import / export (JSON) | Shipped |
| — | `IDynamicEntityIndexService` | Shipped |

#### Observability

| # | Feature | Status |
|---|---------|--------|
| — | Structured logging `ILogger` with structured properties | Shipped |
| — | OpenTelemetry metrics `DataSurfaceMetrics` | Shipped |
| — | Distributed tracing `DataSurfaceTracing` | Shipped |
| — | Health checks (DB, contracts, dynamic metadata) | Shipped |
| — | Audit logging `IAuditLogger` | Shipped |

#### Tooling & Infrastructure

| # | Feature | Status |
|---|---------|--------|
| — | OpenAPI / Swashbuckle typed schema generation | Shipped |
| — | Source generator — compile-time DTOs | Shipped |
| — | Feature flag presets (Minimal, Standard, Full) | Shipped |
| — | Bulk operations `POST /api/{route}/bulk` | Shipped |
| — | Streaming NDJSON `GET /api/{route}/stream` | Shipped |
| — | `CompiledQueryCache` (cache infrastructure) | Shipped |
| — | Query result caching `IQueryResultCache` | Shipped |

</details>

---

## v1.1.0 — Validation & Data Integrity `NEXT`

*Close the gap between what the contract declares and what the runtime enforces. Every validation rule, default value, and computed field becomes live.*

| # | Feature | Status |
|---|---------|--------|
| — | **Field validation enforcement** — MinLength, MaxLength, Min, Max, Regex, AllowedValues | Planned |
| — | **Default values on create** — apply `DefaultValue` from `[CrudField]` | Planned |
| — | **Computed field evaluation** — evaluate `ComputedExpression` at read time | Planned |
| 5.8 | **Unique constraint pre-check** — `[CrudField(Unique = true)]`, composite support, 409 on conflict | Planned |
| — | **Tenant isolation enforcement** — `[CrudTenant]` auto-filter + auto-set | Planned |
| — | **Field projection** — `?fields=` applied to response mapping | Planned |
| 2.2 | **Unified `AddDataSurface()` registration** — single call replaces manual service wiring | Planned |
| 2.5 | **Roslyn analyzers** — warn on missing `[CrudKey]`, invalid flag combos, quick-fixes | Planned |

---

## v1.2.0 — Integrations & HTTP Layer `PLANNED`

*Wire up every HTTP-layer feature that has a configuration surface today but no runtime implementation.*

| # | Feature | Status |
|---|---------|--------|
| — | **Rate limiting** — connect `EnableRateLimiting` to ASP.NET Core middleware | Planned |
| — | **API key authentication** — invoke `IApiKeyValidator` on requests | Planned |
| — | **Webhook publishing** — invoke `IWebhookPublisher` after mutations | Planned |
| 9.5 | **Webhook management endpoints** — CRUD for `WebhookSubscription` via admin API | Planned |
| — | **Import / export endpoints** — `GET /export`, `POST /import` (JSON + CSV) | Planned |
| — | **Dynamic entity index in search** — use index data in `?q=` queries | Planned |
| 3.4 | **Per-resource CORS** — `[CrudAuthorize(CorsPolicy = "...")]` | Planned |
| 4.1 | **Compiled query integration** — wire `CompiledQueryCache` into `EfDataSurfaceCrudService` | Planned |
| 7.7 | **CI pipeline** — PR build + test, code coverage, benchmark regression, NuGet validation | Planned |

---

## v1.3.0 — Query & Protocol Enhancements `PLANNED`

*Richer queries, new pagination modes, aggregation support, and protocol-level improvements.*

| # | Feature | Status |
|---|---------|--------|
| 1.7 | **Cursor-based pagination** — keyset pagination via `?cursor=`, `nextCursor`/`prevCursor` | Planned |
| 1.10 | **COUNT endpoint** — `GET /api/{resource}/$count` returning a plain integer | Planned |
| 1.11 | **Aggregation queries** — `$aggregate?group=status&sum=amount`, server-side GROUP BY | Planned |
| 1.5 | **JSON Patch (RFC 6902)** — `application/json-patch+json` with contract validation | Planned |
| 1.9 | **Batch requests** — `POST /api/$batch`, multi-resource, transactional | Planned |
| 1.6 | **Conditional creates** — `If-None-Match: *` on POST, 412 on duplicate | Planned |
| 4.4 | **Response compression** — Brotli / gzip for large responses and exports | Planned |
| 8.7 | **API versioning** — `/api/v1/users`, version-to-contract mapping, deprecation markers | Exploring |

---

## v2.0.0 — Next Generation `FUTURE`

*New protocol endpoints, alternative configuration models, real-time capabilities, and a full admin experience. May include breaking changes.*

### New Protocols

| # | Feature | Status |
|---|---------|--------|
| 1.1 | **GraphQL endpoint** — auto-generated schema from contracts, queries + mutations | Exploring |
| 1.2 | **gRPC service** — `.proto` generation from contracts, shared pipeline | Exploring |
| 1.3 | **Real-time updates** — SignalR / WebSocket push on CRUD events, per-resource channels | Exploring |
| 1.4 | **OData compatibility** — `$filter`, `$select`, `$expand`, `$orderby` as alternative syntax | Exploring |

### Developer Experience

| # | Feature | Status |
|---|---------|--------|
| 2.1 | **Fluent configuration API** — `builder.Resource<T>().Field(x => x.Name).Required()` | Exploring |
| 2.3 | **Typed client SDK generator** — C# HttpClient wrappers + optional TypeScript types | Exploring |
| 2.4 | **`dotnet new` templates** — `datasurface-api`, `datasurface-entity` scaffolds | Exploring |
| 2.6 | **Hot-reload for dynamic contracts** — live refresh on `EntityDef` changes, `ContractChanged` event | Exploring |

### Security & Compliance

| # | Feature | Status |
|---|---------|--------|
| 3.7 | **OAuth2 scope mapping** — map CRUD operations to scopes (`users:read`, `users:write`) | Exploring |
| 3.5 | **Field-level encryption** — `[CrudField(Encrypted = true)]` + `IFieldEncryptor` | Exploring |

### Data Management

| # | Feature | Status |
|---|---------|--------|
| 5.1 | **Change data capture** — entity versioning, `GET /{id}/history`, temporal queries | Exploring |
| 5.3 | **Schema migrations** — auto-generate EF migrations from contract changes, CLI command | Exploring |
| 5.6 | **Cross-backend expansion** — expand dynamic ↔ EF Core relations | Exploring |

### Admin & Tooling

| # | Feature | Status |
|---|---------|--------|
| 9.1 | **Admin dashboard** — Blazor/React UI for contracts, entities, queries, audit logs, metrics | Exploring |
| 9.2 | **CLI tool `dotnet-datasurface`** — list, validate, export-schema, scaffold | Exploring |
| 9.3 | **Dynamic entity migration tooling** — DDL generation, rollback for definition changes | Exploring |

### Ecosystem

| # | Feature | Status |
|---|---------|--------|
| 8.1 | **Event bus integration** — MassTransit / MediatR domain events after CRUD operations | Exploring |
| 8.3 | **NoSQL backends** — MongoDB / Cosmos DB via `StorageBackend.MongoDb` | Exploring |
| 8.2 | **Dapper backend** — `StorageBackend.Dapper` for raw SQL, read-heavy workloads | Exploring |
| 8.4 | **Message queue integration** — RabbitMQ / Azure Service Bus / Kafka publish after mutations | Exploring |

---

## Backlog `UNSCHEDULED`

*No version target. Items are pulled into a release based on community feedback and priority shifts.*

### API & Protocol

| # | Feature |
|---|---------|
| 1.8 | **HATEOAS links** — `_links` in responses (self, next, prev, related) |
| 8.5 | **File / blob attachments** — `FieldType.File`, `IFileStorage`, pre-signed URLs |
| 8.6 | **Notification integration** — email / SMS / push on CRUD events via `INotificationSender` |

### Developer Experience

| # | Feature |
|---|---------|
| 2.7 | **Middleware pipeline visualization** — diagnostic endpoint showing hooks, filters, cache layers |
| 2.8 | **Contract diff / changelog** — structured diff, breaking change detection, CI gates |
| 3.1 | **Field masking / PII redaction** — `[CrudSensitive]`, role-based masking |

### Security & Compliance

| # | Feature |
|---|---------|
| 3.2 | **IP allowlisting / denylisting** — per-resource or global IP restrictions |
| 3.3 | **Request signing (HMAC)** — signature-based auth for machine-to-machine |
| 3.6 | **Audit log retention** — auto-purge, `RetentionDays`, archive to cold storage |

### Performance & Scalability

| # | Feature |
|---|---------|
| 4.2 | **Query cost analysis** — complexity estimation, reject expensive queries |
| 4.3 | **Multi-database routing** — read replicas, per-resource connection routing |
| 4.5 | **Background write queue** — async writes, `202 Accepted` with job ID |
| 4.6 | **Connection pooling optimization** — `AddDbContextPool` guidance, pool saturation health check |
| 4.7 | **Partial response caching** — per-entity cache by `{resource}:{id}:{etag}` |

### Data Management

| # | Feature |
|---|---------|
| 5.2 | **Soft-delete recovery** — `POST /{id}/restore`, `?includeDeleted=true` |
| 5.4 | **Data seeding** — seed data via contracts or `ISeedDataProvider` |
| 5.5 | **Archival policies** — `[CrudResource(ArchiveAfterDays = 365)]`, cold storage |
| 5.7 | **Cascade delete rules** — `[CrudRelation(OnDelete = CascadeMode.SetNull)]` |
| 5.9 | **Optimistic offline sync** — conflict resolution, client version vectors |

### Observability & Diagnostics

| # | Feature |
|---|---------|
| 6.2 | **Slow query logging** — threshold-based, include generated SQL |
| 6.3 | **Request / response logging** — full payload logging, per-resource, sensitive field redaction |
| 6.4 | **Custom metric tags** — user-defined dimensions on OpenTelemetry metrics |
| 6.5 | **Error rate alerting** — threshold events via `IWebhookPublisher` |
| 6.6 | **OpenTelemetry logs exporter** — OTLP-native log correlation with traces |

### Testing & Quality

| # | Feature |
|---|---------|
| 7.1 | **In-memory test server** — `DataSurfaceTestServer.Create<T>()`, SQLite in-memory |
| 7.2 | **Contract snapshot testing** — JSON snapshots, CI verification |
| 7.3 | **Fuzz testing** — random filter/sort/expand strings against query parser |
| 7.4 | **Load test suite** — end-to-end HTTP benchmarks, concurrent load, CI regression tracking |
| 7.5 | **Test data builders** — `TestData.User().WithEmail("x@y.com").Build()` fluent API |
| 7.6 | **Mutation testing** — Stryker.NET on validation, security, query engine |

### Documentation & Samples

| # | Feature |
|---|---------|
| 10.1 | **Sample projects** — Minimal API, Multi-tenant SaaS, Dynamic CMS, Microservice |
| 10.2 | **Migration guide** — traditional controllers → DataSurface, before/after |
| 10.3 | **Performance tuning guide** — caching, compiled queries, indexing, benchmark interpretation |
| 10.4 | **Security hardening guide** — production checklist, OWASP alignment |
| 10.5 | **Architecture Decision Records** — key design rationale in `/docs/adr/` |
| 10.6 | **Interactive API docs** — enhanced OpenAPI with examples, Redoc / Scalar |
| 9.4 | **API playground** — interactive explorer in admin panel, contract-aware auto-complete |

---

## Feature Count

| Version | Shipped | Planned | Exploring | Total |
|---------|---------|---------|-----------|-------|
| v1.0.0 | 55 | — | — | 55 |
| v1.1.0 | — | 8 | — | 8 |
| v1.2.0 | — | 9 | — | 9 |
| v1.3.0 | — | 7 | 1 | 8 |
| v2.0.0 | — | — | 21 | 21 |
| Backlog | — | — | — | 33 |
| **Total** | **55** | **24** | **22** | **134** |
