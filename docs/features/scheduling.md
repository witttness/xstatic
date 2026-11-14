# Scheduling (appointment booking)

Module key `scheduling` (`src/Extatic.Api/Features/Scheduling/`). Lets a static site offer bookable time slots,
take bookings (anonymous or signed-in AppUsers), and let customers view / cancel / reschedule with a manage token.

## Concepts

| Entity (table) | Purpose |
|---|---|
| `BookingCalendar` (`booking_calendars`) | A bookable person or resource. IANA `timezone`, `notify_emails` (staff notifications), `is_active`. Slug unique per App. |
| `AvailabilityRule` (`availability_rules`) | Weekly hours: `day_of_week` (0 = Sunday), `start_time`–`end_time`, local to the calendar zone. Several ranges per day; non-overlapping; end `24:00`/`00:00` = midnight. Replaced as a set. |
| `AvailabilityOverride` (`availability_overrides`) | One per calendar per local date: closed (`is_unavailable`) or custom hours replacing that day's weekly rules. |
| `AppointmentType` (`appointment_types`) | What can be booked on a calendar: duration, buffers, start interval (defaults to duration), min notice (default 120 min), max advance (default 60 days), optional max per day, approval, intake JSON Schema, location / meeting URL, manage-page URL template. Slug unique per App. |
| `Appointment` (`appointments`) | A booking. `starts_at`/`ends_at` (UTC), plus `block_starts_at`/`block_ends_at` including buffers — what actually occupies the calendar. Status `Pending`/`Confirmed`/`Cancelled`/`Completed`/`NoShow`. Only the SHA-256 of the manage token is stored. |

All types on one calendar share its availability: a booking of any type blocks overlapping slots of every type.

## Slot generation

For a type over local dates `[from, to)` (max 62 days), in the requested `timezone` (default: calendar zone):

1. For each calendar-local date, take the override (closed / custom hours) or the weekly rules.
2. Step through each window by `slot_interval_minutes` on the local wall clock, converting each start to UTC:
   - a start that does not exist (DST spring-forward gap) yields **no slot**;
   - an ambiguous start (DST fall-back) maps to its **first occurrence** (earlier UTC instant), so each wall-clock
     start yields at most one slot;
   - the appointment must end by the window's end instant (real elapsed minutes); buffers may extend outside it.
3. Drop slots before `now + min_notice` (bypassed for staff), after `now + max_advance_days`, on local days where the
   type already has `max_per_day` Pending/Confirmed bookings, and whose buffered block overlaps any Pending/Confirmed
   appointment on the calendar.

Output is grouped by local date in the requested zone; each slot has UTC `starts_at`/`ends_at` and `local_time`.
"Now" comes from the injected `TimeProvider`.

## Double-booking protection

A PostgreSQL exclusion constraint (`SchedulingSchema.UpSql`) forbids overlapping `[block_starts_at, block_ends_at)`
ranges for Pending/Confirmed appointments on the same calendar. A violation (SQLSTATE `23P01`) becomes
`409 That time slot is no longer available`. The requested start is also re-validated against freshly generated slots
inside the booking transaction. Types with `max_per_day` serialise their bookings with a row lock on the type, so the
cap holds under concurrency. Status changes lock the appointment row. Reschedule updates the row in place: the old
block is released and the new one taken atomically; any failure leaves the original untouched.

> **Migration note:** EF cannot model the constraint. The combined migration must call
> `migrationBuilder.Sql(SchedulingSchema.UpSql)` after creating `appointments` (and `DownSql` in `Down`). It needs
> `btree_gist` (trusted extension, created by the SQL).

## Lifecycle

| From | Action | To | Who |
|---|---|---|---|
| — | book | `Confirmed` (or `Pending` when the type requires approval) | customer, staff (staff may pick either) |
| Pending | confirm | Confirmed | staff |
| Pending | decline | Cancelled | staff |
| Pending/Confirmed | cancel | Cancelled | customer (before min notice), staff |
| Pending/Confirmed | reschedule | same status, new time | customer (before min notice, to a valid slot), staff (min notice bypassed) |
| Confirmed | complete / no-show | Completed / NoShow | staff |

Invalid transitions return 409.

## Emails and webhooks

Staged in the same transaction as the change (`IEmailQueue`, `WebhookDispatcher.StageAsync`).

| Event | Customer email (category) | Staff email (`scheduling.staff`) | Webhook |
|---|---|---|---|
| Booked | `scheduling.booked` (confirmed or "request received") with manage link | yes | `appointment.booked` (+ `appointment.confirmed` when confirmed immediately) |
| Approved | `scheduling.approved` | — | `appointment.confirmed` |
| Declined | `scheduling.declined` | — | `appointment.cancelled` |
| Cancelled | `scheduling.cancelled` | yes | `appointment.cancelled` |
| Rescheduled | `scheduling.rescheduled` | yes | `appointment.rescheduled` (with `previous_starts_at`) |
| Completed | — | — | `appointment.completed` |
| 24 h before | `scheduling.reminder` | — | — |

Times are rendered in the customer's `timezone` when supplied, otherwise the calendar's, always naming the zone
(e.g. `Monday, 2 March 2026, 09:00–09:30 (Europe/Berlin)`). User content is HTML-encoded. Staff emails set
Reply-To to the customer. The manage link (`manage_url_template` with `{token}` replaced) is only in the booking
email — the raw token is never stored, so later emails cannot include it.

**Reminders:** `AppointmentReminderWorker` (hosted service, every minute) claims Confirmed appointments starting
within 24 h with a conditional `UPDATE … SET ReminderSentAt` inside a transaction that also stages the email —
exactly once across replicas. Appointments booked/confirmed/rescheduled less than 24 h ahead are marked as reminded
immediately (their confirmation email serves as the reminder).

## Client API — `api/{app_public_id}/scheduling/…`

Policy `ClientAnonymousOrAppUser` unless stated.

| Method | Route | Rate limit | Notes |
|---|---|---|---|
| GET | `types` | ClientReads | Active types on active calendars |
| GET | `types/{slug}` | ClientReads | |
| GET | `types/{slug}/slots?from=YYYY-MM-DD&to=YYYY-MM-DD&timezone=IANA` | ClientReads | `{ timezone, days: [{ date, slots: [{ starts_at, ends_at, local_time }] }] }` |
| POST | `types/{slug}/appointments` | ClientAnonymousWrites | `{ starts_at, name, email, phone?, timezone?, data }` → 201 `{ appointment, manage_token }`. Signed-in AppUsers are linked and may omit name/email (profile used). 409 slot taken, 422 validation / intake schema. |
| GET | `appointments/{token}` | ClientReads | Customer view (no internal notes); `meeting_url` only when Confirmed; `can_cancel` / `can_reschedule` |
| GET | `appointments/{token}/calendar.ics` | ClientReads | `text/calendar`, METHOD:PUBLISH, UTC times |
| POST | `appointments/{token}/cancel` | ClientAnonymousWrites | `{ reason? }` |
| POST | `appointments/{token}/reschedule` | ClientAnonymousWrites | `{ starts_at }` |
| GET | `me/appointments` | ClientReads | `AuthenticatedAppUser`; the user's bookings |

A token from another App returns 404.

## Platform API — `api/apps/{app_slug}/scheduling/…`

Reads: `AppAnyAccess`. Configuration writes: `AppOwnerOrAdmin`. Appointment actions and export: `AppOwnerOrEditor`.

| Method | Route |
|---|---|
| GET, POST | `calendars` |
| GET, PUT, DELETE | `calendars/{id}` (DELETE → 409 when it has appointments; deactivate instead) |
| PUT | `calendars/{id}/rules` — `{ rules: [{ day_of_week, start_time, end_time }] }` replace-all |
| GET, POST | `calendars/{id}/overrides` (`?from&to` dates) |
| PUT, DELETE | `calendars/{id}/overrides/{overrideId}` |
| GET | `calendars/{id}/preview-slots?from&to[&type_id][&duration=30]` — with `type_id`: that type's slots (min notice bypassed); without: raw availability |
| GET, POST | `types` (`?calendar_id`) |
| GET, PUT, DELETE | `types/{id}` (DELETE → 409 when it has appointments) |
| GET | `appointments?from&to&calendar_id&type_id&status=Pending,Confirmed&page&page_size` (from/to are UTC instants on `starts_at`) |
| GET | `appointments/export.csv` (same filters, ≤ 10 000 rows) |
| POST | `appointments` — manual booking `{ appointment_type_id, starts_at, name, email, phone?, timezone?, data?, notes?, confirmed? }` |
| GET, PATCH | `appointments/{id}` (PATCH `{ notes }`) |
| POST | `appointments/{id}/confirm`, `/decline`, `/cancel` (`{ reason? }`), `/reschedule` (`{ starts_at }`), `/complete`, `/no-show` |

## Dashboard

`features/scheduling`: Appointments agenda (grouped by day, times in each calendar's zone and labelled; date range,
calendar, type and status filters; confirm modals for actions; reschedule via slot picker; CSV export), New booking
(slot picker on `preview-slots`), Calendars (details, weekly-hours grid, date overrides, 7-day availability preview),
Appointment types, Integration panel with a `fetch()` example.

## Example

```js
const API = 'https://api.example.com/api/app_xxxx/scheduling';
const tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
const { days } = await fetch(`${API}/types/consult/slots?from=2026-03-02&to=2026-03-09&timezone=${tz}`).then(r => r.json());
const res = await fetch(`${API}/types/consult/appointments`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ starts_at: days[0].slots[0].starts_at, name: 'Casey', email: 'casey@example.com', timezone: tz, data: {} }),
});
const { appointment, manage_token } = await res.json();
```
