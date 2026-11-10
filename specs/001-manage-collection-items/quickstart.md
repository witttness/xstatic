# Quickstart Validation: Manage Collection Items

Use this guide after implementation. The expected API shapes and roles are in
`specs/001-manage-collection-items/contracts/platform-items.md`; Item version and delivery
states are in `specs/001-manage-collection-items/data-model.md`.

## Prerequisites and setup

- .NET 10 SDK, Node.js, Yarn, Docker, and the repository's local development dependencies.
- A local App owner login. The Compose/Dex setup in `README.md` provides
  `dev@extatic.local` / `abc123` for local development.
- A second accepted collaborator login for role checks, or run the corresponding automated
  authorization tests instead.

From the repository root:

```bash
docker compose up -d
cd src/extatic-dashboard
yarn install
yarn start
```

Open `http://localhost:4180` for the authenticated dashboard. In an environment that does
not auto-apply development migrations, run `dotnet ef database update` from
`src/Extatic.Api` before testing. Keep the dashboard server running for the manual scenarios.

## Automated checks

From the repository root:

```bash
dotnet build src/Extatic.Api
dotnet test tests/Extatic.Api.Tests/Extatic.Api.Tests.csproj
cd src/extatic-dashboard
yarn test --watch=false
yarn build
```

The API test set must include App and Collection isolation, owner/admin/editor/viewer access,
Client AppUser ownership on update/delete, platform-only object validation with unchanged
Client input behavior, schema validation, stale-version and simultaneous-save rejection, shared
version advancement after Client edits, and durable webhook recovery. Use a relational test database
for concurrency-token and transaction claims; in-memory tests alone do not prove those paths.
The dashboard tests must cover raw parsing, form rendering and array editing, lossless mode
switching, schema fallback, validation feedback, viewer controls, and draft preservation.

## Manual scenarios

### 1. Collection without a schema

Create or open an App and a Collection with no schema. Open **Items** from the Collection list.
The empty state and **New Item** action appear for an owner/editor. Create an Item using raw
JSON such as `{ "title": "First item" }`.

Expected: the Item appears on the first page with its identifier, preview, and timestamps.
The form mode is absent. Empty text, malformed JSON, and a JSON array are rejected without
creating an Item. Open the Item, change its title, save, and reopen it; the updated value and
version are visible.

### 2. Collection with a schema

Create another Collection using this sample schema:

```json
{
  "type": "object",
  "required": ["title"],
  "properties": {
    "title": { "type": "string", "description": "Display title" },
    "priority": { "type": "integer", "default": 1 },
    "published": { "type": "boolean" },
    "status": { "type": "string", "enum": ["draft", "live"] },
    "tags": { "type": "array", "items": { "type": "string" } },
    "details": {
      "type": "object",
      "properties": { "summary": { "type": "string" } }
    }
  }
}
```

Open **New Item**. Expected: form and raw modes are available; the form shows labels,
description, required title, default priority, boolean, enum, nested summary, and add/remove
tag controls. Create a valid Item in each mode. Switch between modes after entering data and
confirm no values disappear. Submit missing title or wrong priority type; saving is blocked
with a path and reason. Edit an existing Item and confirm it is validated again.

Add a Collection with a schema shape the form cannot safely represent, such as a root `oneOf`
composition. Expected: raw mode remains usable, and the screen explains the form fallback.
The server still validates the submitted Item against that schema.

### 3. List and permission checks

Populate a Collection with more than 20 Items. Expected: the list shows total count and page
position, and paging shows each Item once while the dataset is unchanged. An accepted viewer
can list and inspect Items but sees no New/Edit controls. A direct write request as that viewer
is denied. An Item ID from another App or Collection cannot be read or edited in this route.
With two AppUsers in the same App, verify one cannot update or delete the other's Item through
the Client API, while the owner can still update and delete their own Item.

### 4. Simultaneous edit

Open the same Item in two browser tabs. Save a change in tab A. Without reloading tab B, save
a different change there.

Expected: tab B receives a conflict message and keeps its unsaved draft; tab A's data remains
stored. Reloading tab B fetches the latest data and version. The rejected save creates no
update webhook. Repeat with a Client API edit between the dashboard load and save to confirm
the shared version invalidates the older dashboard draft.

### 5. Recovery after a partial failure

Run the targeted integration test that stops or interrupts the webhook worker after an Item
transaction commits but before a delivery completes. Restart processing.

Expected: the Item remains saved once, its pending delivery record is found and processed,
and no delivery record is created for invalid or rejected writes. A network or service save
error leaves the editor draft available and never shows a false success message.

## Outcome review

Record the results against `SC-001` through `SC-005` in `spec.md`. For the two 90% usability
criteria, use at least 10 first-attempt participants or documented equivalent sessions; for
the 100% correctness criteria, report the tested cases and any failures. The paging scenario
uses 100 seeded Items and a 30-second target.

## Implementation verification (2026-11-06)

- SC-001: Automated creation coverage passed for schema-backed and schema-less modes, raw JSON
  parse rejection, server object/schema validation, form fields, defaults, nested objects, and
  array editing. The 90% first-attempt usability measure needs participant sessions.
- SC-002: Relational tests passed for rejected create/update, stale versions, simultaneous
  writes, and no delivery on rejected writes. No failures in the tested cases.
- SC-003: Relational tests passed for App and Collection lookup isolation, accepted viewer role
  enforcement, Client AppUser ownership, and no Attachment mutation on denied delete.
- SC-004: The 100-Item relational paging check passed across five pages of 20 without a
  duplicate, within the 30-second target. Dashboard paging and viewer controls passed component
  tests. A live browser navigation check remains useful for visual acceptance.
- SC-005: Automated edit coverage passed for populated draft, lossless mode switching, schema
  errors, stale-save conflict with retained draft, viewer read-only state, and successful
  version advancement. The 90% first-attempt usability measure needs participant sessions.
- Recovery scenario: A committed pending webhook log was claimed after worker construction,
  delivered with the expected signature, and marked successful. Expired leases were also
  reclaimed. This is the automated equivalent of interrupting and restarting the worker.

Webhook delivery logs continue to report status code, attempt count, retry time, and success
time. New delivery attempts leave `response_body` null, including failed HTTP responses, so a
remote server cannot echo credentials into the dashboard log response. Consumers that used
`response_body` should use the status and retry fields for diagnostics; existing stored log
rows are not rewritten.
