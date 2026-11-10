---

description: "Dependency-ordered implementation tasks for collection Item management"
---

# Tasks: Manage Collection Items

**Input**: Design documents from `specs/001-manage-collection-items/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`,
`contracts/platform-items.md`, `contracts/dashboard-items.md`, `quickstart.md`

**Tests**: Included for authorization, tenant isolation, validation, concurrency, webhook
recovery, and editor behavior as required by the constitution and plan. Tests need not be
written before implementation.

**Organization**: Tasks are grouped by user story and ordered by their real dependencies.
Read the contracts and data model before changing request or persistence behavior.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel with adjacent ready tasks in different files.
- **[Story]**: Maps to the P1/P2/P3 user stories in `spec.md`.
- All task paths are relative to the repository root.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add shared types and a meaningful API test harness to the existing projects.

- [X] T001 [P] Define the shared Item response, `version`, paged result, create/update request, and problem-error types in `src/extatic-dashboard/src/app/core/models/item.model.ts`, matching `contracts/platform-items.md` and the API's snake_case JSON.
- [X] T002 [P] Replace the placeholder test with reusable API test setup for authenticated App roles and a relational database in `tests/Extatic.Api.Tests/Extatic.Api.Tests.csproj`, `tests/Extatic.Api.Tests/ApiTestFixture.cs`, and `tests/Extatic.Api.Tests/UnitTest1.cs`; keep database tests isolated and runnable via `dotnet test tests/Extatic.Api.Tests`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish shared persistence, capability, and delivery behavior before story work.

**CRITICAL**: Complete this phase before starting user-story implementation.

- [X] T003 Add `Item.Version` and `WebhookDeliveryLog.ClaimedUntil` in `src/Extatic.Api/Domain/Entities/Item.cs` and `src/Extatic.Api/Domain/Entities/WebhookDeliveryLog.cs`, configure concurrency and due-delivery indexes in `src/Extatic.Api/Data/EntityConfigurations/ItemConfiguration.cs` and `src/Extatic.Api/Data/EntityConfigurations/WebhookDeliveryLogConfiguration.cs`, and generate the EF Core migration in `src/Extatic.Api/Migrations/` plus `src/Extatic.Api/Migrations/AppDbContextModelSnapshot.cs`; preserve these data-model constraints verbatim: "Required, initial value 1 for new and migrated Items; increment once for each successful update through either API surface" and "ClaimedUntil (nullable timestamp)".
- [X] T004 [P] Return current-member `can_manage_items` from `GET /api/apps` and `GET /api/apps/{app_slug}` by updating `src/Extatic.Api/Dtos/AppDtos.cs`, `src/Extatic.Api/Dtos/DtoMappers.cs`, `src/Extatic.Api/Services/AppService.cs`, and `src/Extatic.Api/Controllers/Platform/AppsController.cs`; true only for owner or accepted admin/editor, false for accepted viewer.
- [X] T005 [P] Add the `can_manage_items` field to `src/extatic-dashboard/src/app/core/models/app.model.ts` so dashboard controls use server-provided capability rather than collaborator-list inference.
- [X] T006 Add the additive `version` field to shared Item responses in `src/Extatic.Api/Dtos/ItemDtos.cs` and `src/Extatic.Api/Dtos/DtoMappers.cs`; preserve the existing Client Item authentication and request bodies.
- [X] T007 Convert pending/retry delivery processing to atomic claim-and-lease recovery in `src/Extatic.Api/Webhooks/WebhookDispatcher.cs`, `src/Extatic.Api/Webhooks/WebhookRetryWorker.cs`, and `src/Extatic.Api/Webhooks/WebhookDeliveryWorker.cs`; new Item records use `AttemptNumber = 0`, `NextRetryAt <= now`, and `ClaimedUntil = null`, expired claims become eligible again, signed payloads stay stable, and the existing retry/deactivation limit remains effective.
- [X] T008 Make shared Item create/update writes persist subscribed `item.created`/`item.updated` delivery records in the same transaction as the Item in `src/Extatic.Api/Services/ItemService.cs`; enforce the current Collection schema while preserving existing Client accepted JSON shapes, advance `Version` once on success, reject an optional platform expected-version mismatch as 409, translate EF concurrency conflicts to 409, and create no delivery record for rejected writes.
- [X] T009 Enforce `Item.AppUserId` equality with the authenticated AppUser before Client update or delete in `src/Extatic.Api/Controllers/Client/ClientItemsController.cs`, and verify own-Item success and another AppUser's Item rejection without Item or Attachment mutation in `tests/Extatic.Api.Tests/ClientItemOwnershipTests.cs`; preserve Client authentication and request shapes.
- [X] T010 Verify migrated Item version 1, version increments after Client updates, role capability values, transaction rollback, pending-delivery recovery, and no delivery on rejected writes in `tests/Extatic.Api.Tests/ItemPersistenceTests.cs`, `tests/Extatic.Api.Tests/AppCapabilityTests.cs`, and `tests/Extatic.Api.Tests/WebhookDeliveryRecoveryTests.cs`; exercise concurrency and claims against a relational database.

**Checkpoint**: Existing Client Item routes still work, and the shared persistence and
authorization contracts needed by every story are ready.

---

## Phase 3: User Story 1 - Create an Item (Priority: P1)

**Goal**: An owner/admin/editor can create schema-valid Item data through a derived form or
raw JSON; a schema-less Collection offers only raw JSON.

**Independent Test**: Open the New Item route for Collections with and without a schema,
create an Item in each available mode, inspect the created Item through the platform detail
operation, and confirm invalid input creates neither Item nor webhook delivery. The Items list
navigation is completed in US2.

### Implementation for User Story 1

- [X] T011 [P] [US1] Add platform `POST` and `GET {item_id}` operations in `src/Extatic.Api/Controllers/Platform/PlatformItemsController.cs`; use `AppOwnerOrEditor` for create and `AppAnyAccess` for detail, resolve Collection from `CurrentApp.Id` and Item from Collection ID, require object data only for platform create, return 201 with platform `Location`, 200 for detail, and the contract's 400/403/404/422 errors.
- [X] T012 [P] [US1] Parse Collection schemas into a bounded, lossless form description in `src/extatic-dashboard/src/app/features/items/schema-form/schema-form.model.ts` and `src/extatic-dashboard/src/app/features/items/schema-form/schema-form.util.ts`; support object properties, required fields, descriptions, defaults, string/number/integer/boolean/enum values, nested objects, and homogeneous arrays, and return a raw-only explanation for unsupported structural or composition shapes.
- [X] T013 [US1] Render the schema-derived controls, including nested objects and permitted array add/remove actions, with labels, descriptions, defaults, required cues, and keyboard-accessible errors in `src/extatic-dashboard/src/app/features/items/schema-form/schema-form.component.ts`; do not drop unmodeled draft properties or claim full client-side schema validation.
- [X] T014 [US1] Build the New Item editor in `src/extatic-dashboard/src/app/features/items/item-editor/item-editor.component.ts`; provide raw JSON for every Collection, show the form only when T012 marks the schema representable, keep one lossless object draft across mode switches, reject empty/malformed/non-object raw text, render server pointer/message errors, preserve the draft after failures, and show confirmation plus the created Item after 201.
- [X] T015 [US1] Add the New Item route before the generic Collection settings route in `src/extatic-dashboard/src/app/app.routes.ts` and load the Collection and App capability in `src/extatic-dashboard/src/app/features/items/item-editor/item-editor.component.ts`; hide/deny write affordances for viewers while the API remains authoritative.

### Verification for User Story 1

- [X] T016 [US1] Verify create/detail policy, cross-App and cross-Collection scoping, dashboard-created `app_user_id = null`, object/schema validation, 201/422 response contracts, and no webhook record on failed create in `tests/Extatic.Api.Tests/PlatformItemCreateTests.cs`.
- [X] T017 [US1] Verify form controls and fallback, array editing, raw parse errors, lossless mode switching, viewer deep-link behavior, draft preservation, and creation feedback in `src/extatic-dashboard/src/app/features/items/schema-form/schema-form.util.spec.ts` and `src/extatic-dashboard/src/app/features/items/item-editor/item-editor.component.spec.ts`.
- [X] T018 [US1] Run the US1 create scenarios from `specs/001-manage-collection-items/quickstart.md` and record any discrepancies in `specs/001-manage-collection-items/quickstart.md`; confirm the new Item via the platform detail response until US2 adds the Items list.

**Checkpoint**: Item creation works independently from listing and editing.

---

## Phase 4: User Story 2 - Browse Collection Items (Priority: P2) — MVP completion

**Goal**: Any authorized App member can browse only the selected Collection's Items, with
bounded paging, useful previews, and viewer-safe controls.

**Independent Test**: Seed 100 Items through existing data paths, open the Items view without
creating a new Item, page through them, and inspect an Item as a viewer.

### Implementation for User Story 2

- [X] T019 [US2] Bound `page >= 1` and `1 <= page_size <= 100` with default 20, return total count, and sort by `CreatedAt` descending then `Id` descending in `src/Extatic.Api/Services/ItemService.cs` and `src/Extatic.Api/Dtos/ItemDtos.cs`; reject invalid query values as 400.
- [X] T020 [US2] Add platform `GET` list to `src/Extatic.Api/Controllers/Platform/PlatformItemsController.cs` with `AppAnyAccess`, scoped Collection lookup, and the snake_case `{items,total,page,page_size}` response from `contracts/platform-items.md`.
- [X] T021 [US2] Build the Items list in `src/extatic-dashboard/src/app/features/items/items-list/items-list.component.ts`; show loading/error/empty states, Item identifier, bounded readable data preview, created/updated times, total count, current page, paging controls, and an Item detail link.
- [X] T022 [US2] Add the Items route ahead of the generic Collection settings route in `src/extatic-dashboard/src/app/app.routes.ts`, add a distinct Items link beside Edit Collection in `src/extatic-dashboard/src/app/features/collections/collections-list/collections-list.component.ts`, and show New/Edit actions only when `can_manage_items` is true in `src/extatic-dashboard/src/app/features/items/items-list/items-list.component.ts`.

### Verification for User Story 2

- [X] T023 [US2] Verify list bounds, stable ordering for equal creation times, total and page metadata, empty result, accepted viewer access, and App/Collection isolation in `tests/Extatic.Api.Tests/PlatformItemListTests.cs`.
- [X] T024 [US2] Verify list loading/error/empty/paging states, 100-Item navigation, readable preview, and viewer-only controls in `src/extatic-dashboard/src/app/features/items/items-list/items-list.component.spec.ts`.
- [X] T025 [US2] Run the US2 browse and role scenarios in `specs/001-manage-collection-items/quickstart.md`; confirm an Item created in US1 appears in the correct Collection and no cross-tenant Item appears.

**Checkpoint**: Item browsing is independently usable with seeded data, and the US1 create
journey now leads to a populated Items view.

---

## Phase 5: User Story 3 - Edit an Item (Priority: P3)

**Goal**: Authorized managers can edit Item data using the same entry modes, while a stale
draft cannot overwrite a newer Item.

**Independent Test**: Seed an Item, open it directly, save a valid edit, reopen it, then edit
from two tabs and verify that the second stale save receives 409 and retains its draft.

### Implementation for User Story 3

- [X] T026 [US3] Add platform `PUT {item_id}` with required positive `version` and object `data` in `src/Extatic.Api/Controllers/Platform/PlatformItemsController.cs` and `src/Extatic.Api/Dtos/ItemDtos.cs`; use `AppOwnerOrEditor`, reject non-object data only on this platform route, preserve Item ID/Collection/AppUser attribution and attachments, return incremented version on 200, 409 for stale saves, and 422 for current-schema errors.
- [X] T027 [US3] Extend the shared editor in `src/extatic-dashboard/src/app/features/items/item-editor/item-editor.component.ts` to load existing data/version, offer the same allowed modes, submit expected version, keep invalid historical Items viewable, preserve the draft on 409/403/422/network failure, and require an explicit reload after a conflict; add viewer read-only behavior on direct Item links.
- [X] T028 [US3] Add the Item detail/edit route ahead of the generic Collection settings route in `src/extatic-dashboard/src/app/app.routes.ts` and connect Items-list detail/Edit links in `src/extatic-dashboard/src/app/features/items/items-list/items-list.component.ts`.

### Verification for User Story 3

- [X] T029 [US3] Verify update policy and scope, current-schema validation, historical invalid-Item reads, version advancement, stale and simultaneous update 409 behavior, unchanged Item/ownership/webhook state after rejection, and Client-update invalidation of a dashboard draft in `tests/Extatic.Api.Tests/PlatformItemUpdateTests.cs` and `tests/Extatic.Api.Tests/ItemConcurrencyTests.cs`.
- [X] T030 [US3] Verify populated edit modes, valid save and refreshed version, changed-schema errors, viewer read-only view, 409 reload prompt, and draft preservation after permission/schema/network errors in `src/extatic-dashboard/src/app/features/items/item-editor/item-editor.component.spec.ts`.
- [X] T031 [US3] Run the US3 edit and simultaneous-tab scenarios in `specs/001-manage-collection-items/quickstart.md` and verify the Client API authentication and update request shape still work.

**Checkpoint**: All three user stories and the accepted stale-save clarification work.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verify the integrated feature and close contract, accessibility, and recovery risks.

- [X] T032 Check role changes after page load, cross-App/Collection IDs, no credential exposure, accessible labels/error announcements, and problem-response mapping against `specs/001-manage-collection-items/contracts/platform-items.md` and `specs/001-manage-collection-items/contracts/dashboard-items.md`; correct any mismatch in the affected `src/Extatic.Api/` or `src/extatic-dashboard/src/app/features/items/` files.
- [X] T033 Run `dotnet build src/Extatic.Api`, `dotnet test tests/Extatic.Api.Tests`, `yarn test`, and `yarn build` as described in `specs/001-manage-collection-items/quickstart.md`; resolve failures in their owning `src/Extatic.Api/`, `src/extatic-dashboard/src/app/`, or `tests/Extatic.Api.Tests/` files.
- [X] T034 Execute the webhook interruption/restart and 100-Item paging checks in `specs/001-manage-collection-items/quickstart.md`, record results against SC-001 through SC-005 in `specs/001-manage-collection-items/quickstart.md`, and fix verified gaps in the corresponding `src/Extatic.Api/` or `src/extatic-dashboard/src/app/` files.

---

## Dependencies & Execution Order

### Phase Dependencies

1. **Setup (Phase 1)** can start immediately; T001 and T002 touch separate projects.
2. **Foundational (Phase 2)** starts after setup. T003 precedes T006–T008. T009 follows the
   existing Client route and is required by the constitution. T004 and T005 can
   proceed independently, but all foundational checks must pass before story work.
3. **US1 (Phase 3)** starts after foundation. T011 and T012 can start together; the renderer
   follows schema interpretation, and the editor follows the renderer and API contract.
4. **US2 (Phase 4)** follows US1 for the shared controller and navigation files. Its list
   behavior can be validated with seeded Items, independent of the create flow.
5. **US3 (Phase 5)** follows US1 and US2 because it extends the shared controller, editor, and
   Items-list links. Its edit behavior can be validated with seeded Items.
6. **Polish (Phase 6)** follows all three stories.

### User Story Dependencies

- **US1 (P1)**: Depends on foundation. Creates Items without needing the list or edit UI.
- **US2 (P2)**: Depends on the shared controller from US1, but browsing is independently
  testable with seeded data and no create action.
- **US3 (P3)**: Depends on the shared editor and controller from US1 and list links from US2;
  direct edit is independently testable with a seeded Item.

### Within Each User Story

- Complete models and service behavior before endpoints that use them.
- Complete endpoints and form utilities before integrating UI screens.
- Add focused tests at the behavior boundary and run the story's independent check before
  moving to the next priority. Test-first ordering is optional.
- Do not mark a story complete while its role, tenant, validation, or failure-path checks fail.

### Parallel Opportunities

- T001 and T002 are independent setup work.
- T004 and T005 can proceed alongside the persistence work after the contracts are read.
- T011 (API controller) and T012 (schema interpretation) can proceed together after foundation.
- In US2, T019 service paging and the initial T021 list presentation can begin separately once
  the response contract is fixed; connect the endpoint and UI before verification.
- Test files and UI files in a story can be developed separately after their relevant contract
  and behavior settle. Shared files such as `PlatformItemsController.cs`,
  `item-editor.component.ts`, and `app.routes.ts` must be coordinated sequentially.

## Parallel Example: User Story 1

After foundation, T011 in `src/Extatic.Api/Controllers/Platform/PlatformItemsController.cs`
and T012 in `src/extatic-dashboard/src/app/features/items/schema-form/` can proceed in parallel.
T013 then uses T012's form description; T014 uses the API and renderer.

## Parallel Example: User Story 2

After US1, T019 in `src/Extatic.Api/Services/ItemService.cs` and the initial T021 view in
`src/extatic-dashboard/src/app/features/items/items-list/items-list.component.ts` can proceed
in parallel. T020 and T022 connect the list endpoint, route, and controls.

## Parallel Example: User Story 3

After US2, the backend update contract in T026 and editor behavior in T027 can be developed
in separate files, then joined through T028 and verified by T029–T031. T026 and T027 both
depend on the version contract established in foundation.

## Implementation Strategy

### MVP First (User Stories 1 and 2)

1. Complete Setup and Foundation.
2. Complete US1 and its independent create checks using the platform detail response.
3. Complete US2 so a newly created Item is findable in its Collection's Items view.
4. Validate the combined creation and browsing journey before calling it the MVP.

### Incremental Delivery

1. Add US3 editing and stale-save handling after the create-and-browse MVP.
2. Run the full contract and quickstart checks before considering the complete feature done.

## Notes

- `[P]` marks tasks with different files and no unfinished direct dependency.
- The existing Client API remains separate; shared Item version and delivery behavior still
  require regression checks.
- The new Item migration and webhook recovery are required for the constitution gates, not
  optional polish.
