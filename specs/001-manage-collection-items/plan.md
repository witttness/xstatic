# Implementation Plan: Manage Collection Items

**Branch**: `001-manage-collection-items` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-manage-collection-items/spec.md`

## Summary

Add Collection-scoped Item browsing, creation, and editing to the dashboard. A schema-aware
editor offers generated controls for common object schemas and raw JSON for every schema;
schema-less Collections use raw JSON only. New platform Item operations use existing App role
policies and server-side validation. Add an Item version for optimistic concurrency and a
durable webhook handoff so Item writes remain safe under simultaneous edits and partial failure.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript 6.0; Angular 22.2.3 in the dashboard manifest

**Primary Dependencies**: ASP.NET Core, EF Core 10 with Npgsql, JsonSchema.Net 9.4.1; Angular
forms, signals, router, Tailwind CSS. No new schema-form dependency planned.

**Storage**: PostgreSQL for Apps, Collections, Items, and durable webhook delivery records;
Item data and Collection schemas remain JSONB. One EF Core migration for Item version and
webhook delivery claim state.

**Testing**: xUnit service/controller tests, API integration tests for App scoping, roles, and
AppUser Item ownership,
relational integration tests for version and transaction behavior, Angular unit tests for form
translation, mode switching, errors, and role-based controls; relevant builds and manual
end-to-end scenarios.

**Target Platform**: Existing ASP.NET Core API and browser-based dashboard; local Docker
Compose stack for end-to-end validation.

**Project Type**: Full-stack web application with separate Platform and Client API surfaces.

**Performance Goals**: A member can locate an Item among 100 using five 20-Item pages within
30 seconds; creation and editing can be completed within 3 minutes in the specified usability
tests. Item pages are bounded to 100 records maximum.

**Constraints**: App and Collection isolation, owner/admin/editor write and viewer read access,
AppUser ownership checks on Client update/delete, current-schema validation on create/update,
object-only data for platform writes, lossless draft switching, 409 stale-save rejection,
recoverable webhook delivery, and unchanged Client authentication, request shapes, and accepted
data shapes.

**Scale/Scope**: One new dashboard Items list and one shared create/edit experience per
Collection; platform list/detail/create/update operations plus Client Item ownership checks;
no dashboard Item delete, attachment management, search, bulk action, or schema editing.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Pre-research gate | Post-design gate |
|-----------|-------------------|------------------|
| Tenant and authorization boundaries | PASS with required work: platform App role patterns exist, but Client update/delete must enforce AppUser Item ownership. | PASS by design: contracts require App-scoped platform reads, editor-level writes, and Client ownership checks; capability data controls UI affordances. |
| API and data contracts | PASS: distinct Client and Platform surfaces retained; schema validation and migration needs identified. | PASS: platform endpoints, status codes, error shapes, Item version, and compatibility rules are specified in `contracts/`; migration is in `data-model.md`. |
| Reliable side effects | PASS with required design work: current Item commit precedes webhook enqueue and needs recovery. | PASS: transactional delivery records and claimable worker processing close the Item-to-webhook gap. |
| Boundary-focused verification | PASS: authorization, ownership, validation, concurrency, and failure paths identified as required tests. | PASS by design: `quickstart.md` covers role, ownership, tenant, schema, conflict, and webhook recovery scenarios; targeted automated tests are specified here. |
| Simple, consistent design | PASS: existing service/controller and Angular patterns can be extended. | PASS: reuse ItemService, policies, JsonSchemaValidator, forms/signals, and existing delivery logs; no new package or broker. |

**Gate result**: PASS before research and after Phase 1 design. No constitution exception is
needed. The webhook recovery work is justified by the existing partial-failure path.

## Project Structure

### Documentation (this feature)

```text
specs/001-manage-collection-items/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── platform-items.md
│   └── dashboard-items.md
└── tasks.md                 # Created by $speckit-tasks
```

### Source Code (repository root)

```text
src/Extatic.Api/
├── Controllers/Platform/     # New platform Items controller; App capability response
├── Controllers/Client/       # Existing Item operations share version advancement and enforce ownership
├── Services/                 # Item validation, version check, durable event handoff
├── Domain/Entities/          # Item version; webhook delivery claim state
├── Dtos/                     # Platform Item request/response and App capability
├── Data/EntityConfigurations/# Concurrency and delivery indexes
├── Migrations/               # Schema migration
├── Webhooks/                 # Pending delivery claim and recovery
└── Middleware/               # Existing 409/422 error mapping or narrow refinement

src/extatic-dashboard/src/app/
├── app.routes.ts             # Collection Items routes
├── core/models/             # Item and capability response models
├── core/services/           # Existing API client extension if needed
└── features/items/          # Items list and shared create/edit editor

tests/Extatic.Api.Tests/      # Role, scoping, validation, conflict, recovery tests
```

**Structure Decision**: Extend the existing API and dashboard projects. Keep Item business
rules in services, route and authorization logic in platform controllers, and local UI state in
signals and form controls. Reuse the existing Item and delivery entities with a migration.

## Design sequence

1. Add the migration and shared Item version behavior. Ensure both Platform and Client Item
   updates advance the token, while only Platform updates require an expected version. Enforce
   AppUser ownership on existing Client update/delete routes.
2. Add App capability data and Collection-scoped Platform Item contracts with authorization,
   bounded paging, object-only platform input, current-schema validation, and 409 conflict
   handling. Preserve the Client API's accepted data shapes.
3. Make Item event delivery records transactional with Item saves, then add pending-claim and
   lease-based recovery to the worker. Confirm rejected writes produce no event.
4. Add dashboard navigation, Items list, and the shared editor. Implement raw JSON first, then
   schema-derived controls and lossless mode switching; render field-level errors.
5. Verify isolated user journeys and failure paths with focused API and UI tests plus the
   runnable checks in `quickstart.md`.

## Complexity Tracking

No constitution violations require an exception. The migration and worker changes are needed
to enforce the accepted stale-save and recoverable-side-effect requirements.
