# Contract Definition

This locks the single source of truth your library will run on: a unified Resource Contract that can be produced from either C# attributes (static entities) or EntityDef/PropertyDef JSON (dynamic entities). Everything else (EF mapping helpers, CRUD endpoints, validators, Swagger, hooks, overrides) will consume this contract.

## 1 Core Terms
### Resource

A CRUD-exposed “thing” (entity/table or dynamic entity key), e.g. users, posts, invoices.

### Operation

One of: List, Get, Create, Update, Delete.

### Contract

Normalized metadata describing:

* what’s exposed
* how it’s validated
* how relationships are written/read
* what’s filterable/sortable/expandable
* how authorization and scoping apply

## 2 The Unified Resource Contract
### 2.1 Contract types (language-agnostic)

#### ResourceContract

* `resourceKey` (string): stable identifier (`"User"`, `"Post"`, `"dyn:Asset"`).
* `route` (string): `"users"`, `"posts"`.
* `backend` (enum): `EfCore` | `DynamicJson` | `DynamicEav` | `DynamicHybrid`.
* `key`:
    * `name` (string): `"Id"`
    * `type` (enum): `Int32` | `Guid` | `String` (keep minimal)
* `operations`: map of `OperationContract` for each CRUD op.
* `fields`: list of `FieldContract` (all known fields, including derived).
* `relations`: list of `RelationContract`.
* `query`:
    * `filterableFields` (set)
    * `sortableFields` (set)
    * `defaultSort` (optional)
    * `maxPageSize` (int)
    * `allowQuery` (bool; default true)
* `read`:
    * `expandAllowed` (set of relation names)
    * `maxExpandDepth` (int; default 1)
    * `defaultExpand` (optional set)
    * `fieldsAllowed` (optional allowlist for `fields=`)
* `security`:
    * per-operation policy names (strings) OR a rule object
    * optional row-scope provider name/key (for tenant/org scoping)

#### OperationContract (per resource per operation)

* `enabled` (bool)
* `inputShape` (FieldSelection): which fields can appear in request
* `outputShape` (FieldSelection): which fields can appear in response
* `rules`:
    * required fields
    * immutable fields (cannot be set on Update)
    * computed/read-only fields
* `concurrency`:
    * `mode`: `None` | `RowVersion` | `ETag`
    * `field`: e.g. `"RowVersion"`
    * `requiredOnUpdate` (bool)

#### FieldContract

* `name` (string) — canonical model property name
* `apiName` (string) — external name (defaults to `name`)
* `type` (enum): `String`, `Int32`, `Decimal`, `Boolean`, `DateTime`, `Guid`, `Json`, `Enum`, `StringArray`, `IntArray`, `GuidArray` (keep compact)
* `nullable` (bool)
* `dto` membership flags:
    * `inRead` / `inCreate` / `inUpdate`
    * `filterable` / `sortable`
* `validation`:
    * `requiredOnCreate` (bool)
    * `minLength/maxLength`, `min/max`, `regex`, `enumValues`
* `behavior`:
    * `immutable` (bool)
    * `hidden` (bool) (hard deny)
    * `computed` (bool) (server-controlled)
    * `defaultValue` (optional)
* `storage` (optional):
    * for dynamic backends: `indexed` (bool), `promotedColumn` (string?)

#### RelationContract

* `name` (string): e.g. `"User"`, `"Tags"`
* `kind` (enum): `ManyToOne` | `OneToMany` | `ManyToMany` | `OneToOne`
* `targetResourceKey` (string)
* `fkField` (string?) e.g. `"UserId"` (for many-to-one)
* `join` (optional, for many-to-many):
    * `joinEntityName` or join table info
    * `leftKey/rightKey`
* `read`:
    * `expandAllowed` (bool)
    * `defaultExpanded` (bool)
* `write`:
    * `mode` (enum): `ById` | `ByIdList` | `None` | `NestedDisabled`
    * `writeFieldName` (string): e.g. `"UserId"` or `"TagIds"`
    * `requiredOnCreate` (bool)
* `limits`:
    * `maxItems` (optional, for lists)

#### FieldSelection

* typically represented as allowlist of apiNames for the op.

### 2.2 Canonical JSON representation (for dynamic definitions and debugging)

This is the exact “contract JSON” your runtime can generate and cache:
```json
{
  "resourceKey": "Post",
  "route": "posts",
  "backend": "EfCore",
  "key": { "name": "Id", "type": "Int32" },
  "query": {
    "maxPageSize": 200,
    "filterableFields": ["id", "title", "userId"],
    "sortableFields": ["id", "title"],
    "defaultSort": "-id"
  },
  "read": {
    "expandAllowed": ["user"],
    "maxExpandDepth": 1
  },
  "operations": {
    "List":   { "enabled": true, "outputShape": ["id","title","userId"] },
    "Get":    { "enabled": true, "outputShape": ["id","title","userId","user"] },
    "Create": { "enabled": true, "inputShape": ["title","userId"], "rules": { "requiredOnCreate": ["title","userId"] } },
    "Update": { "enabled": true, "inputShape": ["title","userId"], "rules": { "immutable": ["id"] },
                "concurrency": { "mode": "RowVersion", "field": "rowVersion", "requiredOnUpdate": true } },
    "Delete": { "enabled": true }
  },
  "fields": [
    { "name": "Id", "apiName": "id", "type": "Int32", "inRead": true, "filterable": true, "sortable": true, "immutable": true },
    { "name": "Title", "apiName": "title", "type": "String", "inRead": true, "inCreate": true, "inUpdate": true, "validation": { "requiredOnCreate": true, "maxLength": 200 } },
    { "name": "UserId", "apiName": "userId", "type": "Int32", "inRead": true, "inCreate": true, "inUpdate": true, "validation": { "requiredOnCreate": true }, "filterable": true }
  ],
  "relations": [
    { "name": "User", "kind": "ManyToOne", "targetResourceKey": "User",
      "read": { "expandAllowed": true },
      "write": { "mode": "ById", "writeFieldName": "userId", "requiredOnCreate": true }
    }
  ]
}
```

## 3 How C# attributes map into the Contract

You’ll implement a **ContractBuilder** that reads attributes + EF conventions.

### Required attributes (minimal)

* `[CrudResource("route")]` → creates `ResourceContract`
* `[CrudField(In = ...)]` → creates/updates `FieldContract`
* `[CrudRelation(...)]` → creates/updates `RelationContract`

### EF conventions you rely on

* FK pattern `UserId` + `User` navigation
* collections: `ICollection<Tag>` etc.
* `[ForeignKey]` supported but not required

### Default mapping rules

* If a property has no [CrudField]:
    * default `hidden = true` (safe) **OR** default `inRead = true` (convenient)
    * **Decision (recommended)**: **safe default** = not exposed unless explicitly included, per resource or global option.
* For navigations:
    * not included in Create/Update unless relation explicitly declares `write.mode`.
    * not returned unless `expandAllowed` and requested.

## 4 API Surface Spec (CRUD endpoints, query syntax, behaviors)
### 4.1 Endpoint patterns (Minimal API friendly)

Base prefix: `/api`

For resource route `posts`:

#### List

`GET /api/posts`

* query params:
    * `page` (1-based, default 1)
    * `pageSize` (default 20, max from `ResourceContract.query.maxPageSize`)
    * `sort` (comma list, e.g. `title,-id`)
    * `filter[field]` (simple operators)
    * `q` (optional search string, only across fields marked searchable if you add that)
    * `expand` (optional, allowlisted)
    * `fields` (optional allowlisted projection)

#### Get

`GET /api/posts/{id}`

* supports `expand` and `fields`

#### Create

`POST /api/posts`

* body: JSON object containing only fields allowed by `Create.inputShape`
* relation writes: IDs only (e.g. `userId`, `tagIds`)

#### Update (PATCH-like semantics)

`PATCH /api/posts/{id}` (recommended)

* body: JSON object; only provided fields are applied
* fields not present are not touched
* still validated: cannot include fields not in `Update.inputShape`, cannot change immutable, required concurrency token if enabled

(You may also offer `PUT` but internally treat it the same unless you want strict replacement semantics.)

#### Delete

`DELETE /api/posts/{id}`

body generally not needed; allow optional `?hard=true` if configured

### 4.2 Filtering syntax (safe and limited)

Use a simple, explicit scheme:

* `filter[title]=hello` → string contains (or eq; you decide default)
* `filter[id]=eq:10`
* `filter[price]=gt:100`
* `filter[userId]=in:1|2|3`

Supported operators (Phase 0 decision):

* `eq`, `neq`, `gt`, `gte`, `lt`, `lte`, `contains`, `starts`, `ends`, `in`, `isnull`

Only allowed if field is marked `filterable`.

### 4.3 Sorting

* `sort=title,-id`
    * Only allowed if field is marked `sortable`.

### 4.4 Expand

* `expand=user,tags`
    * Only relations marked `expandAllowed`.
    * Max depth `maxExpandDepth` enforced.
    * No implicit deep expansion.

### 4.5 Response format

List response:

```JSON
{
  "items": [ ... ],
  "page": 1,
  "pageSize": 20,
  "total": 123
}
```
Get response: single object.

## 5 Error and validation spec (problem+json)

Standardize on RFC7807-like responses:

```JSON
{
  "type": "https://datasurface/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "traceId": "00-...",
  "errors": {
    "title": ["Max length is 200"],
    "userId": ["Required"]
  }
}
```

Other canonical types:

* `not-found` (404)
* `forbidden` (403)
* `unauthorized` (401)
* `conflict` (409) for concurrency failures
* `invalid-metadata` (500 at startup only; fail fast)

## 6 Concurrency spec

Support one concurrency mechanism per resource (default `None`):

### RowVersion (EF)

* field: `rowVersion` (base64 in API)
* required on update if enabled
* on mismatch: `409 Conflict`

This will be part of `OperationContract.concurrency`.

## 7 Security and scoping spec
### 7.1 Operation policies

Per resource, per operation:

* `posts.read`
* `posts.create`
* `posts.update`
* `posts.delete`

Contract contains policy names; implementation can integrate with ASP.NET Core authorization.

### 7.2 Row-level scoping

Add optional resource scope hook key:

* e.g. `scopeProvider = "TenantScope"`
which enforces:
* list queries automatically add `WHERE TenantId = currentTenant`
* get/update/delete verify same

(This is essential across many projects.)

## 8 Default Safety Rules (non-negotiable defaults)

These are the defaults your library will enforce unless explicitly relaxed:

**Opt-in exposure**

* Only `[CrudResource]` (or EntityDef exposure flag) becomes an endpoint.

**Field allowlist**

* Only fields explicitly in Create/Update/Read shapes are accepted/emitted.
* Unknown fields → 400.

**No nested graph writes**

* Relations are written only by IDs (`ById` / `ByIdList`).
* Nested objects in writes are rejected by default.

**Controlled expand**

* Expand allowlist + depth limit (default depth = 1).

**Paging required**

* Always paged list results.
* Default `pageSize=20`, max enforced (default max 200).

**Filter/sort allowlists**

* Only predeclared filterable/sortable fields.

**Metadata validation at startup**

* Invalid contracts fail fast with clear diagnostics (missing key, conflicting apiName, circular expand config, etc.)

