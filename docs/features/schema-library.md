# Schema Library

Reusable JSON Schemas (draft 2020-12) that can be picked when editing a Collection's schema.
Module key `schema-library`; code in `src/Extatic.Api/Features/SchemaLibrary/`, dashboard in
`src/extatic-dashboard/src/app/features/schema-library/`.

## Two-level model

| Library | Managed by | Who can pick from it |
|---|---|---|
| **Global** (`scope = global`) | Platform admins (`Platform:AdminEmails`, policy `PlatformAdmin`) | `shared` schemas: every App. `admin_only` schemas: platform admins only (drafts, internal) |
| **App** (`scope = app`) | That App's owner and admins | That App's owner, admins and editors. Never visible to other Apps |

New global schemas start `admin_only` unless created with `visibility: "shared"`. App schemas have no
visibility (`null`).

## Roles matrix

| Action | Platform admin | App owner | App admin | App editor | App viewer | Outsider |
|---|---|---|---|---|---|---|
| Global library CRUD, visibility, archive, delete, usage (`api/admin/schemas`) | yes | – | – | – | – | – |
| App library read: list, get, versions, usage, validate | if a member with editor+ | yes | yes | yes | – | – |
| App library write: create, update, archive, delete | if a member with admin+ | yes | yes | – | – | – |
| Picker (`.../schemas/available`) | sees `admin_only` too (flagged) | shared + own | shared + own | shared + own | – | – |
| Attach provenance to a Collection (create/update collection) | unchanged: owner/admin | yes | yes | – | – | – |

Being a platform admin does not grant access to an App. App routes still need the usual collaborator
role; inside an App, a platform admin also sees `admin_only` global schemas.
Collection create/update keeps its existing `AppOwnerOrAdmin` policy, so **editors can browse the picker
but cannot save a collection's schema**. (The product owner's wording was "any editor". Changing that
means relaxing the collections policy, which is a separate decision.)

## Copy-on-select

Choosing a library schema **copies** its JSON into `Collection.schema`. Collections never reference a
library schema live: a library edit must not silently change how a tenant's Items are validated, and
schema changes only apply to Items saved later.

The Collection records provenance:

- `source_schema_id` (FK → `library_schemas`, `ON DELETE SET NULL`) and `source_schema_version`.
- The DTO also returns `source_schema_name` and `source_schema_latest_version`. Both are null when the source
  was deleted or is no longer visible to this App (for example a Shared schema switched to `admin_only`).

Provenance is checked when it is **set or changed**. The schema must be visible to the caller and not
archived, and the version must exist. Otherwise the response is 422 on `source_schema_id` or
`source_schema_version`. Unknown, invisible and other-App ids get the same error. Re-saving a collection with
unchanged provenance is not re-checked, so collections whose source was later archived or hidden can still be
edited. Send `detach_source_schema: true` to clear provenance. The copied schema stays.

## Update flow (dashboard)

1. Collection form: **Choose from library** opens the picker. It has a search box, groups for "Shared" and
   "This app", and a JSON preview. Choosing a schema fills the textarea and sets provenance.
2. The form shows "Based on *name* v*N*". It adds a **modified** badge when the textarea differs from version
   *N*. The comparison ignores key order: it is checked on load and set again on any manual edit.
3. When `source_schema_latest_version > source_schema_version`, an **Update available (v*M*)** button
   opens a line diff from the current textarea to v*M*. **Apply update** replaces the text and sets
   provenance to v*M*. Nothing is saved until the form is submitted.
4. **Detach** removes provenance. On save the form sends `detach_source_schema: true`.

## Versioning

- `library_schemas.schema` holds the current content. `library_schema_versions` stores an immutable row per
  version, unique on `(library_schema_id, version)`.
- A version is written only when the content changes. Content is compared as canonical JSON (keys sorted), so
  reformatting or reordering keys does not bump the version. Metadata-only edits (name, slug, description,
  category, tags) never bump it. `change_note` is stored on the new version row.
- Updates lock the row (`SELECT … FOR UPDATE`) inside a transaction, so concurrent writers get contiguous,
  unique version numbers. The unique index is the backstop.

## Validation (422)

- The schema must be a JSON object of 256 KB or less.
- `$schema`, if present, must be `https://json-schema.org/draft/2020-12/schema`.
- The schema must build and evaluate with JsonSchema.Net. A fresh `SchemaRegistry` is used, so nothing is fetched.
- **No external references.** Every `$ref`, `$dynamicRef` and `$recursiveRef` must start with `#`. Values under
  `const`, `enum`, `default` and `examples` are data and are not scanned.
- Slugs use the platform rule `[a-z0-9-]{1,100}`. They are unique among global schemas, and unique per App among
  that App's schemas (two partial unique indexes). A conflict returns 409.
- Name is required (200 characters max). Description: 2000 max. Category: 100 max. Tags: up to 20, each 50
  max, normalised to lowercase. Change note: 500 max.
- `POST …/validate` takes `{ schema, sample? }` and returns
  `{ valid, schema_errors: [{pointer, message}], sample_errors: [...] | null }`. The response is always 200.

## API

Global library: `api/admin/schemas`, policy `PlatformAdmin`, global scope only. An App schema id returns 404 here.

| Method | Route | Notes |
|---|---|---|
| GET | `/` | `q`, `category`, `visibility` (`shared`/`admin_only`), `archived` (bool, omit for all), `page`, `page_size` |
| POST | `/` | `{name, slug, schema, description?, category?, tags?, visibility?, change_note?}` |
| GET | `/{id}` | Includes version history (newest first) |
| PUT | `/{id}` | Any subset of `{name, slug, description, category, tags, schema, change_note}`. An empty description or category clears it |
| PUT | `/{id}/visibility` | `{visibility}` |
| POST | `/{id}/archive`, `/{id}/unarchive` | |
| DELETE | `/{id}` | Collections keep their copy; their provenance is cleared |
| GET | `/{id}/versions/{version}` | |
| GET | `/{id}/usage` | Collections across all Apps that point at it, with app slug/name and collection slug/name |
| POST | `/validate` | |

App library: `api/apps/{app_slug}/schemas`. Reads need `AppOwnerOrEditor`; writes need `AppOwnerOrAdmin`. Another
App's schema returns 404.

| Method | Route | Notes |
|---|---|---|
| GET | `/` | `q`, `category`, `archived`, paging |
| POST / PUT / DELETE | `/`, `/{id}` | As above, without visibility |
| POST | `/{id}/archive`, `/{id}/unarchive` | |
| GET | `/{id}`, `/{id}/versions/{version}`, `/{id}/usage` | Usage covers this App's collections only |
| POST | `/validate` | |
| GET | `/available` | Picker list: shared global plus this App's schemas, not archived. Platform admins also get `admin_only`, flagged. `q`, `category`, paging (default 100) |
| GET | `/available/{id}` | Content. 404 for another App's schema, and for `admin_only` unless the caller is a platform admin. Archived schemas are still readable, so existing provenance can be diffed |
| GET | `/available/{id}/versions/{version}` | Same visibility rules |

## Data

- `library_schemas`: Id, Scope (`Global`/`App`), AppId? (FK → apps, cascade), Name, Slug, Description, Category,
  Tags text[], Visibility (`AdminOnly`/`Shared`, null for App), Schema jsonb, Version, IsArchived,
  CreatedByUserId, UpdatedByUserId, timestamps.
  - Check constraint `ck_library_schemas_scope`: Global ⇒ AppId null and Visibility set; App ⇒ AppId set and
    Visibility null.
  - Indexes `ix_library_schemas_global_slug` (Slug WHERE AppId IS NULL) and `ix_library_schemas_app_slug`
    (AppId, Slug WHERE AppId IS NOT NULL).
- `library_schema_versions`: Id, LibrarySchemaId (cascade), AppId? (mirrors parent, FK cascade), Version,
  Schema jsonb, ChangeNote, CreatedByUserId, CreatedAt.
- `collections`: adds SourceSchemaId? (FK, `ON DELETE SET NULL`) and SourceSchemaVersion?.
- Neither library table implements `IAppScoped`, because global rows have no AppId. Both are on the
  `TenantScopingConventionTests` allowlist. Every App-scope query filters by AppId (see `LibrarySchemaAccess`
  and `SchemaLibraryService.InScope`).

## Possible follow-ups

- A workflow for tenants to **propose** an App schema to the global library, with admin review and promotion.
  This is out of scope for now.
- Sidebar link `apps/:appSlug/schemas` ("Schemas"). For now the only entry point is the
  "Manage app schemas" link in the collection form's picker.
- Allowing editors to save collection schemas, if the product owner confirms "any editor".
