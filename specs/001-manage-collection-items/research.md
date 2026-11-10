# Research: Manage Collection Items

## Decision 1: Use platform Item endpoints with existing App role policies

**Decision**: Add dashboard-facing Item list, detail, create, and update operations under the
existing App and Collection route hierarchy. Use `AppAnyAccess` for reads and
`AppOwnerOrEditor` for writes. Resolve the Collection by `CurrentApp.Id` and then resolve each
Item by Collection ID. Expose a current-member `can_manage_items` capability in App responses so
the dashboard can hide write controls from viewers; the server policy remains authoritative.

**Rationale**: `CollectionsController`, `AppContextMiddleware`, `AppRoleHandler`, and
`CollectionService.GetBySlugAsync` already establish the platform trust and tenant boundary.
The existing Client Item endpoints require AppUser credentials and serve a different audience.
App DTOs currently have no member role or capability, so UI-only role inference would be
unreliable. The current App role enum and policy include owner, admin, editor, and viewer.

**Alternatives considered**: Reusing Client endpoints would require AppUser credentials and
blur the two API surfaces. Fetching a full collaborator list to infer the current member's role
would duplicate authorization logic and expose unnecessary team data. A separate capabilities
endpoint would add a round trip for one App-scoped flag.

**Repository evidence**: `src/Extatic.Api/Controllers/Platform/CollectionsController.cs`,
`src/Extatic.Api/Controllers/Client/ClientItemsController.cs`,
`src/Extatic.Api/Auth/Handlers/AppRoleHandler.cs`,
`src/Extatic.Api/Controllers/Platform/AppsController.cs`.

## Decision 2: Add an explicit Item version for stale-save protection

**Decision**: Add a `Version` integer to Item, initialized to 1 and incremented for every
successful Item update through either API surface. Configure it as an EF Core concurrency token.
Include `version` in Item reads; require it in platform update requests. Reject a mismatch with
409 before mutation, and translate a database concurrency conflict at save time to the same
409 response. Do not emit an update webhook for a rejected save. Existing Client update requests
remain accepted without a caller-supplied version but must still advance the shared token.

**Rationale**: `UpdatedAt` is set by the application and is a poor exact concurrency token due
to timestamp serialization and precision. A version survives reloads and lets the database
detect a race between the version check and save. EF Core compares configured concurrency tokens
with their original values during `SaveChanges` and raises `DbUpdateConcurrencyException` if no
row matches. [EF Core concurrency documentation](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

**Alternatives considered**: Last-write-wins contradicts the clarification. A timestamp token
avoids migration but depends on exact round trips and clock precision. Automatic JSON merge
introduces ambiguous conflict semantics. A platform-only version would miss AppUser edits.

**Repository evidence**: `src/Extatic.Api/Domain/Entities/Item.cs`,
`src/Extatic.Api/Data/AppDbContext.cs`, `src/Extatic.Api/Services/ItemService.cs`.

## Decision 3: Keep schema validation canonical on the server

**Decision**: Reuse `JsonSchemaValidator` through `ItemService` for both platform and Client
create/update. Require a JSON object for new platform Item writes, including schema-less
Collections, while preserving the Client API's existing accepted data shapes. The dashboard
parses raw JSON before submit and displays path-level validation errors returned by the API.
The current Collection schema is loaded for every save; the UI's schema snapshot is not the
authority.

**Rationale**: The current service already validates against Collection schemas and returns
pointer/message errors through a 422 response. Client validation improves feedback but cannot
be trusted for persistence. Existing Items remain readable after schema changes, while edits
must satisfy the current schema.

**Alternatives considered**: Duplicating full JSON Schema validation in the browser risks
different results from the server. Relying only on form controls would let raw input bypass
validation and fail on complex schema keywords.

**Repository evidence**: `src/Extatic.Api/Validation/JsonSchemaValidator.cs`,
`src/Extatic.Api/Services/ItemService.cs`,
`src/Extatic.Api/Middleware/GlobalExceptionHandler.cs`.

**Authorization boundary**: Existing Client Item update/delete routes resolve the App and
Collection but do not compare `Item.AppUserId` with the authenticated AppUser ID. Enforce that
ownership check before either mutation, without changing Client authentication or request
shapes. Dashboard writes continue to use platform App role policies.

## Decision 4: Use a conservative schema form with lossless mode switching

**Decision**: Use the dashboard's existing Angular forms and signals, with a small schema
interpreter for object properties, required fields, descriptions, defaults, primitive values,
enums, nested objects, and homogeneous arrays. The editor keeps one JSON draft as the source
of truth. Form controls serialize into it without dropping unknown properties. Switching to
raw JSON shows the complete draft; switching back requires parseable object data that the form
can represent without loss. If a structural or composition keyword cannot be handled safely,
offer raw JSON and explain why the form is unavailable. The server validates all schema
constraints at save time, including those not expressed as client controls.

**Rationale**: The project has Angular forms but no schema-form package. A limited interpreter
fits the requested common shapes, while raw JSON retains full schema expressiveness. Angular
documents building dynamic forms from a metadata model; this informs the control structure,
not the JSON Schema validation semantics. [Angular dynamic forms guide](https://angular.dev/guide/forms/dynamic-forms).

**Alternatives considered**: A broad new form library adds a dependency and still needs a
compatibility policy for unsupported schema features. Rendering unknown composition keywords
as ordinary fields risks changing data meaning. Discarding unknown fields on mode switch risks
data loss.

**Repository evidence**: `src/extatic-dashboard/package.json`,
`src/extatic-dashboard/src/app/features/collections/collection-form/collection-form.component.ts`,
`src/extatic-dashboard/src/app/core/services/api.service.ts`.

## Decision 5: Make Item webhook handoff recoverable

**Decision**: For Item creation and updates, persist delivery records for active webhook
subscriptions in the same database transaction as the Item mutation. Mark each delivery pending
for a worker to claim. The worker processes pending and retryable records with a claim/lease,
so a process crash or queue failure does not strand a saved Item's event. Preserve signed
payloads and existing retry/deactivation behavior. A rejected validation or concurrency save
creates no delivery record.

**Rationale**: Today `ItemService` commits the Item before `WebhookDispatcher.EnqueueAsync`
creates delivery logs and writes to an in-memory channel. A failure between those steps can
leave an Item saved while the request reports failure and the webhook is missing. The current
retry worker only scans rows with `NextRetryAt`, so it cannot recover an initial delivery lost
from the channel. A transactional delivery record gives the side effect a durable recovery path.

**Alternatives considered**: Continuing the current channel-only handoff violates the
constitution's partial-failure rule. A separate general-purpose message broker adds
infrastructure for a problem the existing database can handle. Reversing the order would risk
sending a webhook for an Item transaction that later rolls back.

**Repository evidence**: `src/Extatic.Api/Services/ItemService.cs`,
`src/Extatic.Api/Webhooks/WebhookDispatcher.cs`,
`src/Extatic.Api/Webhooks/WebhookRetryWorker.cs`,
`src/Extatic.Api/Domain/Entities/WebhookDeliveryLog.cs`.

## Decision 6: Keep listing bounded and predictable

**Decision**: Reuse the existing paged Item service query, bound page size to 1–100 with a
default of 20, and add Item ID as a tie-breaker after creation time. Return total and page
metadata. The dashboard's initial Items view uses 20 per page.

**Rationale**: The service already uses pagination, but currently has no bounds and sorts only
by creation time. A stable order and finite page size make the 100-Item acceptance scenario
predictable and limit work per request.

**Alternatives considered**: Loading the whole Collection scales poorly. Search and custom
sorting are outside this feature.

**Repository evidence**: `src/Extatic.Api/Services/ItemService.cs`,
`src/Extatic.Api/Dtos/ItemDtos.cs`.

## Resolved unknowns and dependencies

- Stack in the repository: .NET 10, EF Core/Npgsql, PostgreSQL JSONB, Angular 22.2.3,
  Angular forms/signals, Tailwind, xUnit, and Angular's unit-test target. The dashboard package
  manifest takes precedence over the older Angular 21 note in `AGENTS.md`.
- No new schema-form package is required for the bounded form grammar above.
- One EF Core migration is required for Item version and durable delivery claim state.
- Platform API contracts and dashboard navigation are new; Client authentication, request
  bodies, and accepted data shapes remain intact, while update/delete ownership is enforced.
- Meaningful API tests must be added; the test project currently contains only a placeholder.
