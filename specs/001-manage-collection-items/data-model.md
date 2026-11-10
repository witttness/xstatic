# Data Model: Manage Collection Items

## Existing relationships

| Entity | Relevant fields | Relationship and rule |
|--------|-----------------|-----------------------|
| App | `Id`, `Slug`, `OwnerId` | Tenant boundary. Platform requests resolve the App from the route and authenticated user. |
| Collaborator | `AppId`, `UserId`, `Role`, `AcceptedAt` | Accepted viewer can read; accepted editor/admin and owner can write. |
| Collection | `Id`, `AppId`, `Slug`, `Schema` | Resolve by both `AppId` and `Slug`; current schema governs every Item save. |
| Item | `Id`, `CollectionId`, `AppUserId`, `Data`, `CreatedAt`, `UpdatedAt` | Belongs to one Collection. `AppUserId` may be null for dashboard-created Items and is preserved on edit. |
| Webhook | `Id`, `AppId`, `Events`, `IsActive` | Active subscriptions determine delivery records for a committed Item event. |
| WebhookDeliveryLog | `Id`, `WebhookId`, `EventType`, `Payload`, `AttemptNumber`, `NextRetryAt`, `SucceededAt` | Persistent record of a subscribed delivery. |

No new top-level Item entity is needed. Platform Item writes require object data stored in the
existing JSONB column; Client writes retain their current accepted JSON shapes. App-scoped
access follows `App -> Collection -> Item`; Item IDs alone never grant access. The dashboard's
`can_manage_items` flag is derived from the current member's App role and is not stored.

## Item version addition

| Field | Type | Rule |
|-------|------|------|
| `Version` | Positive 64-bit integer | Required, initial value 1 for new and migrated Items; increment once for each successful update through either API surface; configured as a concurrency token. |

The database update must match both Item ID and the version originally read. For a platform
edit, the request's expected version must also match the version loaded for that Item. Any
mismatch returns 409 without changing Item data or creating an update delivery record. A
successful update returns the incremented version. `UpdatedAt` remains display metadata and
is not used as the concurrency token.

The migration backfills existing Items to version 1 and adds the required column and
concurrency mapping. Client Item update requests remain valid without a version field; their
shared service update increments the token so they are visible to platform stale-save checks.

## Item validation and lifecycle

| Transition | Preconditions | Result |
|------------|---------------|--------|
| Platform create | App and Collection resolved; owner/editor policy; data parses to a JSON object; current schema passes if present. | New Item in selected Collection, `AppUserId = null`, `Version = 1`, timestamps set, `item.created` delivery records committed with it. |
| Read/list | App and Collection resolved; any accepted App role or owner. | Item data and metadata returned; no schema validation required for historical data. |
| Platform update | Same scope and write policy; data is an object and passes current schema; platform expected version matches. | Existing identity, Collection, and AppUser attribution preserved; data and `UpdatedAt` change; version increments; `item.updated` delivery records committed with it. |
| Client update | AppUser authentication, App/Collection scope, `Item.AppUserId` equals the authenticated AppUser ID, and current schema passes. | Item data and version update; existing accepted Client JSON shapes remain valid. |
| Client delete | AppUser authentication, App/Collection scope, and `Item.AppUserId` equals the authenticated AppUser ID. | Existing deletion and attachment cleanup behavior proceeds only for the owner. |
| Reject | Invalid input, schema error, missing permission, wrong scope, or stale version. | No Item change and no Item event delivery record. |

The API returns path-level schema validation errors where available. A Collection schema
change does not mutate existing Items. An Item created under an older schema remains readable;
it must satisfy the current schema before any edit can be saved.

## Durable delivery state

Extend `WebhookDeliveryLog` with `ClaimedUntil` (nullable timestamp). New Item event delivery
records are written in the same transaction as the Item mutation with `AttemptNumber = 0`,
`NextRetryAt` set to the current time, and `ClaimedUntil = null`. One record is created for each
active webhook subscribed to the event. No subscription means no record is needed.

| State | Field condition | Allowed next state |
|-------|-----------------|--------------------|
| Pending | `SucceededAt = null`, `NextRetryAt <= now`, no active claim | Claimed |
| Claimed | Claim lease is in the future; attempt number incremented | Succeeded, retry scheduled, or lease expiry |
| Retry scheduled | `SucceededAt = null`, future `NextRetryAt`, no claim | Pending when due |
| Succeeded | `SucceededAt` set | Terminal |
| Exhausted | Maximum attempts reached and webhook deactivated by existing policy | Terminal |

Workers must claim due records atomically before sending so two workers do not intentionally
send the same attempt. A claim whose lease expires is eligible for recovery. Delivery remains
at least once: a crash after a recipient accepts a webhook but before success is recorded can
cause a repeat, consistent with retry behavior. The existing signed payload remains stable
across attempts. Add an index supporting due, unclaimed record scanning. Legacy delivery logs
retain their current status; the migration must not replay completed entries.

## UI draft model

The UI holds an in-memory JSON object draft and an entry mode (`form` or `raw`). Raw text is
parsed before it replaces the draft; malformed text stays in the editor and cannot be saved.
Form controls update the same draft without discarding unmodeled properties. The loaded Item
version is held alongside the draft and sent on edit. On a 409, retain both draft and version
until the manager explicitly reloads the latest Item. No draft or schema-form state is stored
in the database.
