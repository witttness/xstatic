# Feature Specification: Manage Collection Items

**Feature Branch**: `N/A (no branch hook configured)`

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "Add UI to support adding items to a collection. With a JSON Schema,
offer a corresponding form or raw JSON entry; without one, offer raw JSON only. Validate new
items against the schema, list entered items if needed, and allow editing."

## Clarifications

### Session 2026-11-05

- Q: If another person updates an Item while you are editing it, what should happen when you save? → A: Reject the stale save and prompt the manager to reload.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create an Item (Priority: P1)

An authorized App manager opens a Collection and creates an Item. If the Collection has a schema,
the manager can use a form derived from it or enter raw JSON. Without a schema, the manager enters
raw JSON. Invalid data is explained before it can be saved.

**Why this priority**: Creating Items is the primary requested capability.

**Independent Test**: Open a Collection with and without a schema, create a valid Item in each
available entry mode, and verify it is stored in the selected Collection.

**Acceptance Scenarios**:

1. **Given** a Collection with a representable schema, **When** an authorized manager opens New
   Item, **Then** the schema-derived form and raw JSON entry are both available.
2. **Given** a Collection without a schema, **When** an authorized manager opens New Item,
   **Then** only raw JSON entry is available.
3. **Given** malformed JSON or data that violates the active schema, **When** the manager tries
   to save, **Then** the Item is not created and the manager sees actionable errors.
4. **Given** valid data, **When** the manager saves, **Then** one Item is created in the selected
   Collection and the manager can find it in the Item list.

---

### User Story 2 - Browse Collection Items (Priority: P2)

An authorized App member opens a Collection's Items view to see entered Items and select one for
inspection or editing.

**Why this priority**: Managers need to find newly created and existing Items.

**Independent Test**: Seed a Collection with Items, open its Items view, and verify the entries
and navigation without creating or editing anything.

**Acceptance Scenarios**:

1. **Given** a Collection with Items, **When** a member opens its Items view, **Then** the view
   shows the Items' identifiers, a readable data preview, and creation and update times.
2. **Given** more Items than fit on one page, **When** the member changes pages, **Then** the
   correct page appears without duplicate or missing entries.
3. **Given** an empty Collection, **When** a member opens its Items view, **Then** an empty state
   explains that no Items exist and offers New Item only to members allowed to create one.

---

### User Story 3 - Edit an Item (Priority: P3)

An authorized App manager opens an existing Item, changes its data, and saves it. The same entry
modes and validation rules used for creation apply to editing.

**Why this priority**: Existing content needs correction and maintenance.

**Independent Test**: Open an existing Item, change it, save, and reopen it to verify the new
data persisted while its identity and Collection stayed the same.

**Acceptance Scenarios**:

1. **Given** an Item in a Collection with a representable schema, **When** an authorized manager
   edits it, **Then** the form is populated from its current data and raw JSON entry is available.
2. **Given** an Item in a Collection without a schema, **When** an authorized manager edits it,
   **Then** raw JSON entry is available and no schema-derived form is shown.
3. **Given** an invalid edit, **When** the manager tries to save, **Then** the stored Item remains
   unchanged and the manager sees actionable errors.
4. **Given** a valid edit, **When** the manager saves, **Then** the updated data appears when the
   Item is reopened and in the Items view.
5. **Given** another person saved a newer version of the Item after the manager opened it,
   **When** the manager tries to save the older draft, **Then** the save is rejected, the newer
   Item remains unchanged, and the manager is prompted to reload before trying again.

### Edge Cases

- If a schema uses constraints that cannot be represented safely as form controls, the user is
  told why the form is unavailable and can use raw JSON; the active schema still governs saving.
- If a schema changes after an Item was created, the existing Item remains viewable. Any edit
  must satisfy the current schema before it can be saved.
- Switching between form and raw JSON preserves the current draft data. If the draft cannot be
  represented in the form, the raw draft stays intact and the user is told why switching failed.
- An empty raw editor, malformed JSON, or a value of the wrong root type cannot be saved.
- If a save fails because of a service or connection error, the draft remains available and no
  success message is shown.
- An Item that was removed or belongs to another Collection cannot be opened or changed through
  the current Collection's view.
- A member whose role changes while the page is open cannot create or edit after losing access.
- If two managers edit the same Item, the first successful save wins; a later save based on the
  older version is rejected without changing the stored Item.
- An AppUser cannot update or delete an Item owned by another AppUser through the Client API.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide an Items view reachable from each Collection in the App
  dashboard without confusing it with Collection settings.
- **FR-002**: The Items view MUST list only Items belonging to the selected App and Collection,
  with an identifier, readable data preview, creation time, and last update time.
- **FR-003**: The Items view MUST support paging, show the total Item count and current page,
  and show a useful empty state when no Items exist.
- **FR-004**: App owners and accepted collaborators with admin or editor access MUST be able to
  create and edit Items through the dashboard. Viewers MUST be able to list and inspect Items but
  MUST NOT be able to create or edit them. These permissions MUST be enforced when data is saved
  or retrieved, not only in the display.
- **FR-005**: Creating an Item MUST associate it with the selected Collection and App. Editing
  MUST preserve the Item's identity, Collection, App, and existing ownership attribution.
- **FR-006**: For a Collection without a schema, create and edit screens MUST offer only raw JSON
  entry. They MUST reject malformed JSON and require an Item data object.
- **FR-007**: For a Collection with a schema that can be represented safely, create and edit
  screens MUST offer both a schema-derived form and raw JSON entry.
- **FR-008**: The derived form MUST represent schema-defined object properties, required fields,
  descriptions, defaults, string, number, integer, boolean, enumeration, nested object, and
  array values. It MUST allow the user to add or remove array entries where the schema permits.
- **FR-009**: If the schema contains a constraint that the form cannot represent without
  changing its meaning, the screen MUST explain that the form is unavailable and provide raw
  JSON entry. It MUST NOT silently omit or relax the constraint.
- **FR-010**: Switching between available entry modes MUST preserve a valid draft. If the raw
  draft cannot be represented in the form, the screen MUST retain it and explain the problem.
- **FR-011**: The system MUST validate every create and edit against the Collection's current
  schema when one exists, regardless of entry mode, and MUST prevent persistence on failure.
  Validation errors MUST identify the affected data path and reason where available.
- **FR-012**: The system MUST prevent empty or malformed raw input from being submitted and MUST
  distinguish input errors, schema errors, permission errors, and save failures in user feedback.
- **FR-013**: A failed save MUST leave the user's draft intact. A successful create or edit MUST
  show confirmation and make the saved Item accessible from the Items view.
- **FR-014**: An Item MUST be viewable even if it no longer conforms to the current Collection
  schema; saving changes MUST require conformance to that current schema.
- **FR-015**: The dashboard's Item operations MUST use the platform user's App permissions and
  MUST NOT require an AppUser credential or grant access to Items in another App or Collection.
- **FR-016**: Editing MUST detect whether the Item changed after the manager opened it. A save
  based on an older version MUST be rejected, preserve the newer stored data and the manager's
  unsaved draft, and prompt the manager to reload before trying again.
- **FR-017**: Client API Item update and delete operations MUST verify that the authenticated
  AppUser owns the Item. Attempts to change another AppUser's Item MUST be denied without
  changing the Item or its Attachments. Client authentication and request shapes MUST remain
  unchanged.

### Key Entities *(include if feature involves data)*

- **App**: Tenant boundary containing Collections and dashboard memberships.
- **Collection**: Group of Items within an App; may have a schema that governs Item data.
- **Item**: Stored data object with an identifier, Collection association, optional AppUser
  ownership attribution, and creation and update times.
- **Dashboard member**: App owner or accepted collaborator whose role determines viewing and
  editing permissions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In usability testing, at least 90% of authorized managers can create a valid Item
  in a Collection with or without a schema within 3 minutes on their first attempt.
- **SC-002**: In acceptance testing, 100% of invalid create and edit attempts leave stored Item
  data unchanged and show a reason the user can act on.
- **SC-003**: In acceptance testing, 100% of tested cross-App, cross-Collection, and insufficient
  role or AppUser ownership access attempts are denied.
- **SC-004**: A member can locate any of 100 seeded Items using the Items view and its paging
  controls within 30 seconds.
- **SC-005**: In usability testing, at least 90% of authorized managers can edit and reopen an
  existing Item with the expected updated data within 3 minutes.

## Assumptions

- The dashboard is for platform users and accepted collaborators. Owner, admin, and editor roles
  may manage Item data; viewer is read-only. Client API authentication and request shapes remain
  unchanged while AppUser Item ownership is enforced.
- Dashboard-created Items have no AppUser owner. Editing an AppUser-created Item preserves its
  existing ownership attribution.
- Dashboard Item deletion, attachment management, sorting, filtering, search, bulk operations,
  and schema editing are outside this feature.
- The dashboard requires object data for Item create and edit. Existing Client API input
  behavior remains unchanged; its current Collection schema still governs Client saves.
- The schema-derived form covers the listed common object properties and value types. Complex
  or ambiguous schema constructs use raw JSON entry with the same save-time validation.
- The existing Item store and schema validation behavior are available to this feature; the
  dashboard needs appropriate authorized access to them.
