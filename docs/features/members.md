# Members (membership management)

Lets a static site run a membership scheme: tiers ("plans") that signed-in AppUsers join, with optional approval,
capacity limits, expiring terms, renewal reminders, member numbers, a member profile and an opt-in member directory.
A member is an existing **AppUser**. The feature has **no payments**: a plan's `price_label` is display text only.

Module: `Features/Members/MembersModule.cs` (key `members`). Code: `src/Extatic.Api/Features/Members/`.

## Data

| Table | Purpose |
|---|---|
| `membership_plans` | `name`, `slug` (unique per app), `description`, `price_label`, `duration_days` (null = never expires), `requires_approval`, `self_signup_enabled`, `max_members` (cap on **Active** members; null = unlimited), `benefits` (jsonb string array), `is_active`, `sort_order`. |
| `memberships` | `app_user_id` (cascade), `plan_id` (restrict), `status`, `member_number`, `profile` (jsonb), `starts_at`, `expires_at`, `approved_at`, `approved_by_user_id`, `cancelled_at`, `notes` (admin-only), `show_in_directory`, `reminders_sent` (int[]). |
| `member_settings` | One per app, created lazily: `profile_schema` (jsonb), `directory_enabled`, `directory_fields` (text[]), `member_number_prefix` (default `M-`), `next_member_number`, `notify_emails` (text[]), `renewal_reminder_days` (int[], default `[30, 7]`). |
| `membership_events` | Audit trail: `type`, `from_status`, `to_status`, `actor_user_id` / `actor_app_user_id`, `detail`. |

Every table carries `AppId` (FK to `apps`, cascade) and every query filters by it; ids from another App are 404.

Constraints doing the real work:

- **One open membership per AppUser** — partial unique index `IX_memberships_AppId_AppUserId_open` on
  `(AppId, AppUserId) WHERE "Status" IN ('Pending','Active','Suspended')`. A user can rejoin after their membership
  is cancelled, rejected or expired (they get a new membership and a new member number).
- **Member numbers** — unique `(AppId, MemberNumber)`; allocated by
  `UPDATE member_settings SET "NextMemberNumber" = "NextMemberNumber" + 1 ... RETURNING` inside the activating
  transaction, so numbers are never duplicated and a rolled-back activation does not consume one. Format:
  `{prefix}{n:D5}`, e.g. `M-00042`. Assigned on **first** activation and kept for the life of that membership.
- **Capacity** — the plan row is locked (`SELECT ... FOR UPDATE`) before counting Active members, on join, approve,
  reinstate, renew-from-expired, admin create and change-plan. Pending applications do not count against the cap.
  Lowering `max_members` below the current count is allowed; it only blocks new activations.

## Status machine

| Action | From | To | Webhook | Email to member |
|---|---|---|---|---|
| join (self-signup, no approval) | — | Active | `member.applied` + `member.activated` | welcome (with member number) |
| join (approval required) | — | Pending | `member.applied` | application received |
| approve | Pending | Active | `member.activated` | welcome (with member number) |
| reject | Pending | Rejected | — | rejected |
| suspend | Active | Suspended | `member.suspended` | suspended |
| reinstate | Suspended | Active | `member.reinstated` | reinstated |
| cancel | Pending / Active / Suspended | Cancelled | `member.cancelled` | cancelled |
| renew | Active / Expired | Active | `member.renewed` | — |
| expire (worker only) | Active | Expired | `member.expired` | expired |

Anything else is `409`. A member may cancel their own membership only while Pending or Active (not while
Suspended). Renew sets `expires_at = max(now, expires_at) + duration_days` and clears `reminders_sent`; a plan with
no duration cannot be renewed (`409`). Admin **change-plan** keeps status and dates (capacity checked if Active).
Self-joins also email every address in `notify_emails` (a "new application" / "new member" notice).
Admin-created memberships (`POST /members`) skip the member-facing application email.

Emails and webhook deliveries are staged with `IEmailQueue` / `WebhookDispatcher.StageAsync` in the same
transaction as the status change, so they are written if and only if the change commits. Emails are skipped when
the AppUser has no valid email address. All interpolated values are HTML-encoded in the HTML body.

Webhook `data`: `id`, `app_user_id`, `email`, `display_name`, `plan_slug`, `status`, `member_number`, `starts_at`,
`expires_at` (no profile, no notes).

### Background job

`MembershipMaintenanceWorker` (hosted service, hourly, first run 30 s after start) runs `MembershipMaintenance`:

- **Expiry** — each Active membership with `expires_at <= now` is claimed with a conditional `UPDATE ... WHERE
  Status = 'Active'` in the transaction that stages its email/webhook/audit row; concurrent replicas cannot both
  claim it.
- **Renewal reminders** — for each offset in `renewal_reminder_days` that has been reached and is not yet in
  `reminders_sent`, the offsets are appended with `UPDATE ... WHERE NOT ("RemindersSent" && offsets) AND
  "ExpiresAt" = <seen value>`; only the claimer sends. When several offsets are due at once (e.g. a 5-day term with
  `[30, 7]`) one email is sent and all are marked. The email states the actual days left.

`IsActiveMemberAsync` also checks `expires_at > now`, so access ends at expiry even before the worker runs.
Time comes from an injectable `TimeProvider` (registered as `TimeProvider.System` with `TryAdd`).

## Client API — `api/{app_public_id}/members/...`

| Method | Route | Auth | Rate limit | Notes |
|---|---|---|---|---|
| GET | `plans` | App id only (ApiKey scheme) | ClientReads | Active plans with `is_full`. |
| GET | `me` | AuthenticatedAppUser | ClientReads | `{ membership: {...} \| null }` — the open membership, else the most recent closed one. |
| GET | `me/access?plan=slug` | AuthenticatedAppUser | ClientReads | `{ active: bool }`. |
| POST | `me` | AuthenticatedAppUser | ClientWrites | `{ plan_slug, profile? }` → 201. 404 unknown/inactive plan, 403 no self-signup, 409 already a member / plan full, 422 profile invalid. |
| PUT | `me/profile` | AuthenticatedAppUser | ClientWrites | `{ profile }`, validated against `profile_schema` → 422 with pointers `profile/...`. |
| PUT | `me/directory` | AuthenticatedAppUser | ClientWrites | `{ show_in_directory }`. |
| POST | `me/cancel` | AuthenticatedAppUser | ClientWrites | 409 when suspended. |
| GET | `directory?page&page_size` | AuthenticatedAppUser | ClientReads | 404 unless `directory_enabled`; 403 unless the caller is an Active member. Returns `{ display_name, fields }` for Active, opted-in members; `fields` holds only the top-level profile keys in `directory_fields`. Never emails, member numbers, notes or ids. |

The member view (`me`) never includes notes, approver ids or the audit trail.

Other features can call `MembershipService.IsActiveMemberAsync(appId, appUserId, planSlug?)` (scoped service).

## Platform API — `api/apps/{app_slug}/members/...`

| Method | Route | Policy |
|---|---|---|
| GET | `plans`, `plans/{id}` | AppAnyAccess |
| POST / PUT / DELETE | `plans`, `plans/{id}` | AppOwnerOrAdmin (DELETE → 409 if the plan has memberships; deactivate instead) |
| GET / PUT | `settings` | AppOwnerOrAdmin |
| GET | `` (list: `status`, `plan_id`, `q` (name/email/member number), `page`, `page_size`) | AppAnyAccess |
| GET | `stats` (`total`, `by_status`, `by_plan`, `expiring_soon` = Active expiring within 30 days) | AppAnyAccess |
| GET | `export.csv` (same filters as the list; no notes) | AppAnyAccess |
| GET | `{id}` (detail with profile, notes and audit trail) | AppAnyAccess |
| POST | `` `{ app_user_id, plan_id, status: "active"\|"pending", profile?, notes? }` | AppOwnerOrEditor |
| POST | `{id}/approve\|reject\|suspend\|reinstate\|renew\|cancel` `{ reason? }` (reason is audited and emailed) | AppOwnerOrEditor |
| POST | `{id}/change-plan` `{ plan_id }` | AppOwnerOrEditor |
| PUT | `{id}/notes` `{ notes }` | AppOwnerOrEditor |

`PUT plans/{id}` and `PUT settings` are full replacements (omitted nullable fields are cleared; plan booleans and
sort order keep their value when omitted). `next_member_number` is read-only.

## Dashboard

`features/members`: members list (status tabs, search, plan filter, stats, row actions, CSV export), member detail
(profile, timeline, actions with optional reason, change plan, admin notes), plans list + editor, and settings
(profile schema textarea with JSON validation, directory options, number prefix, reminders, notify addresses).
