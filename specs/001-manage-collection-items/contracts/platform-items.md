# Contract: Platform Collection Items

Base path: `/api/apps/{app_slug}/collections/{collection_slug}/items`. These endpoints use the
existing platform session identity supplied through the trusted OAuth2 Proxy boundary. They do
not accept an AppUser token or an App API key as authorization for dashboard operations.

All responses use the API's snake_case JSON naming. App resolution, accepted collaborator
membership, Collection lookup, and Item lookup are server-side checks; route IDs do not grant
access. Collection lookup is constrained by the resolved App ID, and Item lookup is constrained
by that Collection ID.

## Authorization and capabilities

| Operation | Owner | Accepted admin | Accepted editor | Accepted viewer |
|-----------|-------|----------------|-----------------|-----------------|
| List/get Items | Allowed | Allowed | Allowed | Allowed |
| Create/update Items | Allowed | Allowed | Allowed | Denied |

The existing `GET /api/apps` and `GET /api/apps/{app_slug}` responses gain a
`can_manage_items: boolean` field for the current platform member. It is true for owner,
accepted admin, and accepted editor; false for accepted viewer. This is an additive response
field for UI affordances. The Item endpoint policies enforce the table independently of it.

## Item representation

```json
{
  "id": "guid",
  "collection_id": "guid",
  "app_user_id": null,
  "data": { "title": "Example" },
  "version": 1,
  "created_at": "2026-11-05T12:00:00Z",
  "updated_at": "2026-11-05T12:00:00Z",
  "attachments": []
}
```

`data` must be a JSON object. `version` is a positive integer and advances once for each
successful update, including updates made through the existing Client API. Adding `version` to
the shared Item response is additive; existing Client request bodies and authentication remain
unchanged. No dashboard operation changes `app_user_id` or attachment ownership.

## List Items

`GET /api/apps/{app_slug}/collections/{collection_slug}/items?page=1&page_size=20`

- Policy: `AppAnyAccess`.
- `page` is at least 1; `page_size` is 1–100, default 20. Invalid values return 400.
- Items are ordered by `created_at` descending, then `id` descending for ties.
- Response 200:

```json
{
  "items": ["Item representation"],
  "total": 42,
  "page": 1,
  "page_size": 20
}
```

An empty Collection returns an empty `items` array and `total: 0`. The dashboard derives
the current page count from `total` and `page_size`.

## Get Item

`GET /api/apps/{app_slug}/collections/{collection_slug}/items/{item_id}`

- Policy: `AppAnyAccess`.
- Response 200: one Item representation, including its current `version`.
- An unknown Item or an Item in a different Collection returns 404.

## Create Item

`POST /api/apps/{app_slug}/collections/{collection_slug}/items`

- Policy: `AppOwnerOrEditor`.
- Request body: `{ "data": { ... } }`.
- Response 201: created Item representation and a `Location` for its platform get URL.
- The server requires object data and validates it against the Collection's current schema
  when one exists. Dashboard-created Items have `app_user_id: null`.
- The Item and subscribed `item.created` delivery records commit together. Validation and
  authorization failures create neither.

## Update Item

`PUT /api/apps/{app_slug}/collections/{collection_slug}/items/{item_id}`

- Policy: `AppOwnerOrEditor`.
- Request body: `{ "data": { ... }, "version": 1 }`. `version` is required.
- Response 200: updated Item representation with incremented `version`.
- The server checks the expected version and current Collection schema. A stale version or
  concurrent database update returns 409, leaves the stored Item unchanged, and emits no
  `item.updated` delivery record.
- Identity, Collection, `app_user_id`, and attachments are not changed by this operation.

## Error contract

| Status | Meaning | Dashboard handling |
|--------|---------|--------------------|
| 400 | Malformed request, missing update version, or invalid page parameters | Show input or request error; keep draft. |
| 401 / login redirect | No platform session | Follow existing authentication flow. |
| 403 | Current member lacks the required App role | Show access denied; keep draft. |
| 404 | App, Collection, or Item unavailable in the current scope | Show not found; do not expose another tenant's data. |
| 409 | Expected Item version is stale | Keep draft, tell manager the Item changed, and offer reload. |
| 422 | Item data is not an object or fails the current schema | Show each available data path and reason; keep draft. |
| 5xx / network failure | Save outcome unavailable | Keep draft and avoid claiming success; reload the Item before retrying an uncertain update. |

Errors follow the existing problem response convention with `status`, `title`, `detail`, and
`instance`. Schema validation includes pointer/message pairs. The current exception handler
wraps these as `errors: { "errors": [{ "pointer": "...", "message": "..." }] }`; the
dashboard must read this existing shape unless a separate compatibility review changes it.

## Compatibility

The Client Item routes under `/api/{app_public_id}/collections/{collection_slug}/items`
retain their authentication, request shapes, and accepted data shapes. Their update/delete
operations enforce `Item.AppUserId` ownership before mutation. The Item service's shared
version increment ensures Client edits invalidate older dashboard drafts. This feature adds
no Item deletion or attachment operation to the Platform API.
