<!--
Sync Impact Report
- Version change: unratified template -> 1.0.0
- Modified principles: none; the prior file contained only template placeholders.
- Added principles: Tenant and Authorization Boundaries; API and Data Contracts;
  Reliable Side Effects; Boundary-Focused Verification; Simple, Consistent Design.
- Added sections: Project Constraints; Development Workflow.
- Removed sections: none; template placeholders were replaced.
- Follow-up TODOs: none.
This report is temporary review material and is to be removed before commit.
-->
# Extatic Constitution

## Core Principles

### I. Tenant and Authorization Boundaries
Every read and write MUST be scoped to the correct App. The API MUST enforce the distinct
permissions of platform users, collaborators, and AppUsers, including ownership checks for
Items and Attachments. OAuth2 Proxy identity headers MUST be trusted only at the configured
proxy boundary. API keys, tokens, and webhook secrets MUST NOT be exposed in logs or responses.
These rules prevent cross-App access and credential disclosure.

### II. API and Data Contracts
The Platform and Client APIs MUST remain distinct. New endpoints MUST specify authentication,
request and response shapes, validation errors, and status codes. Item data MUST be validated
against its Collection schema on create and update when a schema is configured. Database schema
changes MUST use EF Core migrations. Client-visible breaking changes MUST document their impact
and migration path so consumers can adapt safely.

### III. Reliable Side Effects
File uploads and deletes MUST respect Collection settings and ownership. Webhook payloads MUST
be signed, and delivery MUST be retryable and observable. Operations spanning PostgreSQL, blob
storage, or webhook delivery MUST provide a way to detect and recover from partial failure.
External systems cannot participate in one database transaction, so recovery is part of the
feature's behavior.

### IV. Boundary-Focused Verification
Changes to authorization, tenant isolation, validation, API responses, migrations, or external
service failure handling MUST have focused tests at the boundary where the behavior can fail.
The relevant .NET or Angular tests and builds MUST pass before work is considered complete.
Test-first development is not required for every change; verification MUST match the behavior
and risk of the change.

### V. Simple, Consistent Design
Implementations MUST follow the repository's existing .NET, Angular, EF Core, and Angular
Signals patterns. New infrastructure or abstractions MUST have a concrete requirement that
justifies them. Controllers MUST remain thin, with business rules in services. This keeps
behavior discoverable and avoids unnecessary maintenance cost.

## Project Constraints

Extatic is a storage backend for static websites. Its Platform API serves dashboard users
through OAuth2 Proxy identity, while its Client API serves AppUsers through App-scoped
credentials. PostgreSQL and EF Core own relational data and migrations; JSONB stores Item data
and Collection schemas. Azure Blob Storage holds Attachments. These boundaries MUST be
preserved when designing features or changing storage and authentication behavior.

## Development Workflow

Each feature specification MUST identify affected API contracts, authorization rules, data
changes, failure handling, and verification. Plans and task lists MUST account for applicable
principles before implementation. Reviews MUST check compliance with this constitution and
record the reason for any proposed amendment. Relevant tests and builds MUST be run for the
affected .NET or Angular projects before work is marked complete.

## Governance

This constitution takes precedence over conflicting feature plans and task lists. Amendments
MUST state the reason and impact, receive project-owner review, and update the version and
amendment date. Versioning follows semantic versioning: MAJOR for incompatible principle or
governance changes, MINOR for new or materially expanded rules, and PATCH for clarifications
that do not change obligations. The constitution MUST be reviewed when architecture or trust
boundaries change. Feature and code reviews MUST verify applicable principles; exceptions
require an approved constitution amendment rather than an undocumented waiver.

**Version**: 1.0.0 | **Ratified**: 2026-11-05 | **Last Amended**: 2026-11-05
