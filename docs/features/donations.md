# Donations

Donations let a static site take one-time or recurring donations through **the tenant's own payment
provider account**. Extatic never holds funds and never sees card data: donors pay on the provider's
hosted checkout page. The first provider is **Stripe Checkout**, called over raw HTTPS (no Stripe SDK).

Code: `src/Extatic.Api/Features/Donations/` · Dashboard: `src/extatic-dashboard/src/app/features/donations/` ·
Tests: `tests/Extatic.Api.Tests/Features/Donations/`.

## Setup walkthrough (app owner)

1. Dashboard → app → **Donations → Provider setup**.
2. Paste the Stripe **secret key** (`sk_test_…`, or a restricted key `rk_test_…` with Checkout Sessions
   write + Refunds write). Optionally paste the publishable key. Mode (test/live) is derived from the key
   prefix; a test publishable key with a live secret key (or vice versa) is rejected.
3. Copy the **webhook URL** shown on the page
   (`https://<api>/api/<app_public_id>/donations/webhooks/stripe`) into Stripe → Developers → Webhooks →
   *Add endpoint*, and select the events listed on the page:
   `checkout.session.completed`, `checkout.session.async_payment_succeeded`,
   `checkout.session.async_payment_failed`, `checkout.session.expired`, `charge.refunded`.
4. Reveal the endpoint's signing secret (`whsec_…`) and paste it into **Webhook signing secret**. Save.
5. **Campaigns → New campaign**: currency, suggested amounts, min/max (typed in major units, stored in
   minor units), success / cancel URLs on your site, optional goal, dates, donor list, notify emails.
6. On the static site, call the Client API (below). Test end-to-end with Stripe test keys before
   switching to live keys.

Set `Donations:PublicApiBaseUrl` (e.g. `https://api.extatic.example`) in production so the webhook URL
shown in the dashboard is the public one rather than the internal request host.

## Flow

```
Static site              Extatic API                         Stripe (tenant's account)
-----------              -----------                         -------------------------
POST /donations/checkout ──► validate campaign/amount
                             INSERT donation (Pending)
                             POST /v1/checkout/sessions ───────► create session
                             store session id  ◄──────────────── { id, url }
  ◄── { donation_id, checkout_url }
redirect donor ─────────────────────────────────────────────────► hosted checkout page
                                                                  donor pays
                             POST /api/{app}/donations/webhooks/stripe ◄── checkout.session.completed
                             verify Stripe-Signature (tenant secret)
                             INSERT payment_webhook_events (unique → dedupe)
                             lock donation (id + AppId + session id), check amount/currency
                             Succeeded + stage receipt, staff email, donation.succeeded webhook
                             (one transaction) ── 200 ──►
success_url?donation_id=… ◄──────────────────────────────────────── redirect
GET /donations/{id}/status ──► "succeeded"
```

Refunds from the dashboard: `POST …/donations/{id}/refund` locks the donation row, calls
`POST /v1/refunds` (Idempotency-Key per refund), records the refunded amount and stages
`donation.refunded`, all in one transaction. Stripe's later `charge.refunded` event carries the same
cumulative amount and is then a no-op; refunds made directly in Stripe are picked up from that event.

## Endpoints

### Client API — `api/{app_public_id}/donations`

| Method | Route | Auth | Rate limit | Notes |
|---|---|---|---|---|
| GET | `campaigns` | ApiKey (app id in URL) | ClientReads | Active campaigns within their dates, with `raised_minor`, `donor_count`, `goal_minor`, and `recent_donors` when enabled (name or "Anonymous", amount, message — never email) |
| GET | `campaigns/{slug}` | ApiKey | ClientReads | Same, one campaign; 404 if inactive / outside dates |
| POST | `checkout` | ClientAnonymousOrAppUser | ClientAnonymousWrites | `{campaign_slug?, amount_minor, currency?, interval?: one_time\|monthly\|yearly, donor_name?, donor_email?, is_anonymous?, message?}` → `{donation_id, checkout_url}`. 422 validation, 404 campaign, 409 no provider, 502 provider failure |
| GET | `{donation_id}/status` | ApiKey | ClientReads | `{status}` only |
| POST | `webhooks/{provider}` | ApiKey scheme only | **none** (`[DisableRateLimiting]`) | Raw body ≤ 512 KB, signature-verified. 200 processed/duplicate/ignored, 400 bad signature or provider not configured, 404 unknown provider |

`campaign_slug` may be omitted only when the app has exactly one open campaign. Redirect URLs always
come from the campaign; the client cannot supply them. A signed-in AppUser (token for this app) is
linked to the donation and their email is used when `donor_email` is omitted.

### Platform API — `api/apps/{app_slug}/donations`

| Method | Route | Policy |
|---|---|---|
| GET | `providers/{provider}` (includes `webhook_url`, `webhook_events`) | AppOwnerOrAdmin |
| PUT | `providers/{provider}` `{secret_key?, webhook_secret?, publishable_key?, is_enabled?}` | AppOwnerOnly |
| DELETE | `providers/{provider}` | AppOwnerOnly |
| GET / POST | `campaigns` | AppOwnerOrAdmin |
| GET / PUT / DELETE | `campaigns/{id}` | AppOwnerOrAdmin |
| GET | `` (list; `status`, `campaign_id`, `from`, `to`, `q`, `page`, `page_size`) | AppAnyAccess |
| GET | `{id}` | AppAnyAccess |
| POST | `{id}/refund` `{amount_minor?}` (omit for full) | AppOwnerOrAdmin |
| GET | `export.csv` (same filters, max 50 000 rows) | AppAnyAccess |
| GET | `summary` (net total by currency, last 30 days, recurring count, pending count) | AppAnyAccess |

### Webhook events (outgoing, to the tenant's own webhooks)

`donation.succeeded`, `donation.failed`, `donation.refunded`. Data: id, campaign id/slug, amount,
refunded amount, currency, interval, status, donor name (null when anonymous), donor email,
is_anonymous, message, app_user_id, provider, completed_at.

### Emails

`donations.receipt` to the donor (if an email is known) and `donations.notification` to each of the
campaign's `notify_emails`, staged in the same transaction as the status change. User content is
HTML-encoded.

## Data

| Table | Notes |
|---|---|
| `payment_provider_configs` | unique (AppId, Provider). `SecretKeyProtected` / `WebhookSecretProtected` via `ISecretProtector`; only last-4 kept in clear |
| `donation_campaigns` | unique (AppId, Slug); `SuggestedAmountsMinor bigint[]`, `NotifyEmails text[]` |
| `donations` | unique (Provider, ProviderSessionId); CHECK `AmountMinor > 0`, CHECK `0 ≤ RefundedAmountMinor ≤ AmountMinor`; Campaign/AppUser FKs `SET NULL` |
| `payment_webhook_events` | unique (AppId, Provider, ProviderEventId) → idempotency |

Money is always integer minor units (`long`). Zero-decimal currencies (JPY, KRW, …) use the major amount
as-is. Supported currencies: `Currencies.cs` (mirrored in `money.ts`); three-decimal currencies and HUF,
ISK, TWD, UGX are deliberately unsupported for now. Totals (`raised_minor`, `donor_count`, summary) are
computed by query from Succeeded/Refunded donations as `AmountMinor − RefundedAmountMinor`; nothing is
stored as a counter. `donor_count` counts donations with a positive net amount (not distinct people).

## Security model

- **Tenant keys**: encrypted at rest; the API returns only `has_secret_key`, `has_webhook_secret` and the
  last 4 characters. Dashboard secret inputs are write-only and cleared after save. Keys are never logged;
  provider error bodies are never logged or returned (only the HTTP status code is logged).
- **Webhook authenticity**: the tenant comes from the URL; the event is accepted only if its
  `Stripe-Signature` verifies with *that tenant's* signing secret (HMAC-SHA256 of `"{t}.{rawBody}"`,
  constant-time compare, any `v1` may match, ±5 minute tolerance). The raw request body is read directly
  (no model binding). A donation is updated only if its id **and** AppId **and** provider session id match,
  so an event signed by another tenant's account can't touch this tenant's donations. Amount and currency
  from the provider must equal what was recorded; a mismatch marks the donation Failed with no receipt.
- **Idempotency**: the event id is inserted first inside the transaction; a concurrent duplicate blocks on
  the unique index and then returns 200 "duplicate". Donation rows are locked `FOR UPDATE`, and only
  `Pending → …` transitions send receipts, so different events for the same session also succeed once.
- **Refunds**: row lock for the whole refund, so concurrent refunds can't exceed the donation; DB CHECK
  constraint as a backstop.
- **Redirects**: success/cancel URLs are absolute `https` (or `http://localhost`) and only come from the
  campaign config; never from the client request.
- **Abuse**: public checkout uses `ClientAnonymousWrites` (10/min per App+IP); IPs stored only as a keyed hash.
- **Fake provider** (`fake`): resolvable only in the Development environment and in tests.

## What has NOT been verified against live Stripe

Nothing in this feature has been run against the real Stripe API — all tests use a stubbed
`HttpMessageHandler` or the in-process fake provider. Assumptions to confirm with Stripe test keys
before enabling live mode:

1. `POST /v1/checkout/sessions` accepts the exact form fields sent (`mode`, `success_url`, `cancel_url`,
   `client_reference_id`, `metadata[...]`, `customer_email`, `line_items[0][price_data][currency|unit_amount|product_data][name]|recurring][interval]]`,
   `line_items[0][quantity]`, `payment_intent_data[metadata][...]` for one-time and
   `subscription_data[metadata][...]` for subscriptions) and returns `id` and an https `url`.
2. The `Idempotency-Key` header = donation id is acceptable (one key per checkout attempt).
3. No `Stripe-Version` header is pinned, so the account's default API version applies; event payload
   shapes are assumed to be those documented for `checkout.session.*` (`id`, `client_reference_id`,
   `metadata`, `amount_total`, `currency`, `payment_status` = `paid`/`unpaid`/`no_payment_required`,
   `payment_intent`, `subscription`) and `charge.refunded` (`payment_intent`, `amount`,
   `amount_refunded` cumulative, `currency`).
4. Signature scheme: `t=…,v1=…` header, HMAC-SHA256 keyed with the full `whsec_…` string as UTF-8 bytes
   over `"{t}.{rawBody}"`, lowercase hex. (The header format and the "accept unknown entries" rule were
   checked against Stripe's docs; the exact HMAC key handling is from memory of Stripe's libraries.)
5. `POST /v1/refunds` with `payment_intent` + `amount` and a refund `status` of `succeeded`/`pending`
   (treated as accepted) or `failed`/`canceled` (treated as an error).
6. For subscription-mode sessions `payment_intent` is null, so **recurring donations cannot be refunded
   from the dashboard** (409 with a hint to use the Stripe dashboard), and their refunds made in Stripe
   are not reflected (the invoice's payment intent is not stored).
7. **Renewals are not tracked**: only the first payment of a recurring donation is recorded. Handling
   `invoice.paid` / `customer.subscription.deleted` is future work.
8. Stripe's per-currency minimum charge amounts (e.g. 0.50 USD) are not enforced locally; a campaign
   minimum below them will make checkouts fail with 502.
9. The webhook handler processes synchronously (well under Stripe's timeout for normal loads) rather than
   queueing.
