# Events

Event registration for static sites, with capacity limits, waitlists, emails and webhooks.
Module key `events` (`src/Extatic.Api/Features/Events/`). Dashboard: `features/events`.

## Data

| Table | Notes |
|---|---|
| `events` | `AppId`, `Title`, `Slug` (unique per App), `Description?`, `Location?`, `OnlineUrl?`, `StartsAt`/`EndsAt` (UTC), `Timezone` (IANA id), `Capacity?` (null = unlimited), `WaitlistEnabled`, `WaitlistCapacity?` (seats; null = unlimited), `MaxSeatsPerRegistration` (default 1, max 100), `RegistrationOpensAt?`, `RegistrationClosesAt?`, `RegistrationSchema?` (jsonb JSON Schema), `RequireAppUser`, `ManageUrlTemplate?`, `NotifyEmails` (text[], max 10), `Status` (`Draft`/`Published`/`Cancelled`). |
| `event_registrations` | `AppId`, `EventId` (cascade), `AppUserId?` (set null), `Name`, `Email` (trimmed, lowercase), `Seats`, `Data` (jsonb), `Status` (`Confirmed`/`Waitlisted`/`Cancelled`), `ManageTokenHash` (SHA-256; unique), `CheckedInAt?`, `CreatedAt`, `ConfirmedAt?`, `CancelledAt?`, `PromotedAt?`. Partial unique index `(EventId, Email) WHERE "Status" <> 'Cancelled'`. |

## Rules

- **Seats taken** = sum of `Seats` over Confirmed registrations.
- **Registering** requires: event Published, not ended (`EndsAt > now`), inside the registration window,
  a signed-in AppUser when `RequireAppUser`. Then:
  - Confirmed if the seats fit **and nobody is waiting** (strict FIFO: newcomers never jump the waitlist);
  - else Waitlisted if the waitlist is enabled and has room (`waitlisted seats + seats <= WaitlistCapacity`);
  - else 409 `sold_out`.
- **Race-free**: every seat-affecting write (register, cancel, promote, capacity change, publish, cancel event)
  runs in a transaction that first takes `SELECT ... FOR UPDATE` on the event row. The partial unique index
  backs up duplicate-email detection.
- **Promotion**: cancelling a Confirmed registration, raising/removing Capacity, or publishing promotes waitlisted
  registrations FIFO (`CreatedAt`, `Id`), stopping at the first that does not fit. Never for cancelled/ended events.
  Admins may promote a specific waitlisted registration (409 if it doesn't fit, unless `override_capacity`).
- Capacity cannot be lowered below the confirmed seat count (422). Cancelled events are read-only (409).
- Cancelling an event leaves registration statuses as they were (history), emails every Confirmed and
  Waitlisted registrant, and emits `event.cancelled`.

### Registration closed reasons
`not_published`, `cancelled`, `ended`, `not_yet_open`, `closed`, `sold_out`. Returned as
`registration_closed_reason` on event DTOs, and as `reason` on 409 ProblemDetails from registration endpoints.

## Emails (IEmailQueue, staged in the same transaction)

| Category | When | Manage link |
|---|---|---|
| `events.confirmation` | registered (confirmed) | yes |
| `events.waitlisted` | registered (waitlisted) | yes |
| `events.notification` | each new registration → every `NotifyEmails` address | – |
| `events.promoted` | moved off the waitlist | refers to the original email |
| `events.cancellation` | registration cancelled (by registrant or admin) | – |
| `events.event_cancelled` | event cancelled → all active registrants | – |

The manage link is `ManageUrlTemplate` with `{token}` replaced; without a template the raw token is included as
text. Only the hash is stored, so later emails (promotion) cannot repeat the link. User content in HTML bodies is
HTML-encoded.

## Webhooks

`event.registration.created`, `event.registration.waitlisted`, `event.registration.promoted`,
`event.registration.cancelled` (data: registration incl. `event_id`, `event_slug`, `email`, `seats`, `status`,
`data`; never the token) and `event.cancelled` (`id`, `slug`, `title`, `starts_at`, `active_registrations`).

## Client API — `api/{app_public_id}/events`

All require the App id (`ClientAnonymousOrAppUser`); AppUser token optional unless noted.

| Method | Route | Rate limit | Notes |
|---|---|---|---|
| GET | `events?include_past=false&page=1&page_size=20` | ClientReads | Published events, upcoming (`EndsAt > now`) by default, by `starts_at`. |
| GET | `events/{slug}` | ClientReads | Published or cancelled (drafts 404). `seats_remaining`, `waitlist_open`, `registration_open`, `registration_closed_reason`, `is_online`. |
| GET | `events/{slug}/calendar.ics` | ClientReads | RFC 5545 iCalendar (UTC times, escaped + folded). |
| POST | `events/{slug}/registrations` | ClientAnonymousWrites | `{name, email, seats, data}` → 201 `{registration, manage_token}`. Email defaults to the AppUser's. 422 for invalid input / schema; 409 duplicate email or closed (`reason`); 401 when `require_app_user`. |
| GET | `events/registrations/{token}` | ClientReads | Registrant view incl. `waitlist_position`; `online_url` only when confirmed. |
| DELETE | `events/registrations/{token}` | ClientAnonymousWrites | Cancel (204, idempotent); 409 after the event ended. |
| GET | `events/me/registrations` | ClientReads | `AuthenticatedAppUser`: the AppUser's registrations. |

`OnlineUrl`, `NotifyEmails` and `ManageUrlTemplate` are never exposed publicly (the ICS omits the online URL).
Slugs `me` and `registrations` are reserved.

## Platform API — `api/apps/{app_slug}/events`

| Method | Route | Policy |
|---|---|---|
| GET | `events?status=&page=&page_size=` (with stats) | AppAnyAccess |
| POST | `events` | AppOwnerOrEditor |
| GET | `events/{id}` | AppAnyAccess |
| PUT | `events/{id}` (full replace) | AppOwnerOrEditor |
| DELETE | `events/{id}` | AppOwnerOrAdmin |
| POST | `events/{id}/publish`, `events/{id}/cancel` | AppOwnerOrEditor |
| GET | `events/{id}/stats` | AppAnyAccess |
| GET | `events/{id}/registrations?status=&search=&page=&page_size=` | AppAnyAccess |
| GET | `events/{id}/registrations/export.csv?status=&search=` | AppAnyAccess |
| POST | `events/{id}/registrations` `{name, email, seats, data, override_capacity}` → 201 `{registration, manage_token}` | AppOwnerOrEditor |
| POST | `events/{id}/registrations/{rid}/cancel` | AppOwnerOrEditor |
| POST | `events/{id}/registrations/{rid}/promote` `{override_capacity}` | AppOwnerOrEditor |
| PUT | `events/{id}/registrations/{rid}/check-in` `{checked_in}` | AppOwnerOrEditor |

Manual adds ignore the registration window and `RequireAppUser`; with `override_capacity` they are confirmed
regardless of capacity and `MaxSeatsPerRegistration`.

## Validation (422 pointers)

`title`, `slug`, `starts_at`, `ends_at` (after start), `timezone` (IANA), `capacity`, `waitlist_capacity`,
`max_seats_per_registration`, `registration_closes_at`, `registration_schema` (must compile), `online_url`
(http/https), `manage_url_template` (https, contains `{token}`, on an allowed origin when the App has any),
`notify_emails/{i}`. Registrations: `name`, `email`, `seats`, `data` (object ≤ 16 KB), `/data/...` (schema).
