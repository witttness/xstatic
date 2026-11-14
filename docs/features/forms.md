# Forms

Accept submissions from a static site — a plain HTML `<form>` or `fetch()` — with optional JSON Schema
validation, spam trapping, email notifications and a `form.submitted` webhook.

## Concepts

| Field | Meaning |
|---|---|
| `name`, `slug` | Slug is unique per App (`[a-z0-9-]{1,100}`) and appears in the public URL. |
| `schema` | Optional JSON Schema (draft 2020-12). Validated as a schema on save; submissions failing it get `422`. |
| `notification_emails` | Up to 10 addresses. Each gets one email per accepted submission. |
| `success_redirect_url` | Where HTML form posts are sent (`303`) after success. Must be absolute `https://` (or `http://localhost` / `http://127.0.0.1`). Never taken from the request. |
| `honeypot_field` | Name of a hidden field humans leave empty. If filled, the response looks like success but the submission is stored as **spam** with no email or webhook. The field is never stored or validated. |
| `is_active` | Inactive forms return `404` on the public endpoints. |
| `store_submissions` | Default `true`. When `false`, notifications and webhooks still fire but nothing is stored (`id` is `null`). |
| `require_app_user` | Require an AppUser bearer token (`401` when anonymous or the token is for another App). |

Submission statuses: `new`, `read`, `spam`, `archived`. Counts (`submission_count` excludes spam,
`unread_count` = `new`, `spam_count`) are derived, never stored. IPs are stored only as a keyed hash.

## Client API (static site)

Base: `/api/{app_public_id}/forms`. No API key header is needed — the public id in the URL identifies the App.
An `Authorization: Bearer <appuser-jwt>` header is optional (required when `require_app_user`).

| Method | Route | Notes |
|---|---|---|
| GET | `/{slug}` | Public definition `{ name, slug, schema, require_app_user }`. Rate limit: ClientReads. |
| POST | `/{slug}/submissions` | Submit. Rate limit: 10/min per App + IP (ClientAnonymousWrites). |

### Request bodies

* `application/json` — the body must be a JSON object; it is the submission data (no type coercion).
* `application/x-www-form-urlencoded` and `multipart/form-data` (no files — a file part is `400`; an empty
  `<input type="file">` is ignored) — fields become a JSON object of strings. Repeated keys, or keys ending
  in `[]`, become arrays. When the form has a schema, top-level values are coerced to the declared
  `integer` / `number` / `boolean` types (`on`/`true`/`1`/`yes` → `true`; `off`/`false`/`0`/`no` → `false`)
  and a single value becomes a one-element array for `array` properties. An empty string for a non-string
  property is dropped (an empty optional number field means "not supplied").

Maximum body: **64 KB** → `413`.

### Responses

| Situation | Response |
|---|---|
| Success (JSON request, or HTML post with no redirect configured / `Accept: application/json`) | `201 { "id": "…" \| null, "created_at": "…" }` |
| Success, HTML form post, `success_redirect_url` set | `303 See Other`, `Location: <success_redirect_url>` |
| Honeypot filled | identical to success |
| Unknown / inactive form | `404` |
| `require_app_user` and anonymous | `401` |
| Malformed body / unsupported content type / file upload | `400` |
| Schema validation failed | `422 { "errors": [{ "pointer": "/email", "message": "…" }] }` |
| Body over 64 KB | `413` |
| Rate limited | `429` |

On success the submission row, one notification email per address (text + HTML table, HTML-encoded;
`Reply-To` = the submitted `email` field when it is a valid address; category `forms.notification`) and
the `form.submitted` webhook are written in **one transaction** — all or nothing.

### HTML example

```html
<form action="https://api.example.com/api/app_xxxxxxxx/forms/contact/submissions" method="POST">
  <label>Name <input type="text" name="name" required></label>
  <label>Email <input type="email" name="email" required></label>
  <label>Topics
    <select name="topics[]" multiple><option>sales</option><option>support</option></select>
  </label>
  <label><input type="checkbox" name="subscribe"> Subscribe</label>
  <label>Message <textarea name="message"></textarea></label>
  <!-- honeypot: keep hidden and empty -->
  <input type="text" name="website" tabindex="-1" autocomplete="off"
         style="position:absolute;left:-9999px" aria-hidden="true">
  <button type="submit">Send</button>
</form>
```

### JavaScript example

```js
const res = await fetch('https://api.example.com/api/app_xxxxxxxx/forms/contact/submissions', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ name: 'Ada', email: 'ada@example.com', message: 'Hello!' }),
});
if (res.status === 201) {
  const { id, created_at } = await res.json();
} else if (res.status === 422) {
  const { errors } = await res.json(); // [{ pointer, message }]
}
```

For `fetch()` from a browser, add the site's origin to the App's allowed origins.
The dashboard's form page has an **Embed** panel with both snippets pre-filled.

## Platform API (dashboard)

Base: `/api/apps/{app_slug}/forms`.

| Method | Route | Policy |
|---|---|---|
| GET | `/` | AppAnyAccess — list with `submission_count`, `unread_count`, `spam_count` |
| POST | `/` | AppOwnerOrAdmin — create (`201`); duplicate slug `409` |
| GET | `/{id}` | AppAnyAccess |
| PUT | `/{id}` | AppOwnerOrAdmin — full replace (omitted optional fields are cleared) |
| DELETE | `/{id}` | AppOwnerOrAdmin — also deletes its submissions |
| GET | `/{id}/submissions?status=&from=&to=&page=&page_size=` | AppAnyAccess — paged, newest first |
| GET | `/{id}/submissions/export.csv?status=&from=&to=` | AppAnyAccess — CSV (max 50,000 rows, oldest first) |
| GET | `/{id}/submissions/{submission_id}` | AppAnyAccess |
| PATCH | `/{id}/submissions/{submission_id}` | AppOwnerOrEditor — body `{ "status": "read" }` |
| DELETE | `/{id}/submissions/{submission_id}` | AppOwnerOrEditor |
| DELETE | `/{id}/submissions/spam` | AppOwnerOrEditor — bulk-delete spam, returns `{ "deleted": n }` |

Create / update body:

```json
{
  "name": "Contact",
  "slug": "contact",
  "schema": { "type": "object", "required": ["email"], "properties": { "email": { "type": "string" } } },
  "notification_emails": ["you@example.com"],
  "success_redirect_url": "https://example.com/thanks",
  "honeypot_field": "website",
  "is_active": true,
  "store_submissions": true,
  "require_app_user": false
}
```

CSV columns: `submission_id, submitted_at, status, app_user_id, referrer, user_agent`, then the union of
top-level data keys in order of first appearance. Arrays of strings are joined with `; `, other
non-string values are JSON. Cells starting with `= + - @` are prefixed with `'` (formula-injection guard).

A form or submission id belonging to another App always behaves as `404`.

## Webhook: `form.submitted`

Subscribe a webhook to `form.submitted`. Not sent for spam (honeypot) submissions.

```json
{
  "event": "form.submitted",
  "timestamp": "2026-11-14T03:00:00Z",
  "app_id": "…",
  "data": {
    "form_id": "…",
    "form_slug": "contact",
    "submission_id": "…",
    "created_at": "2026-11-14T03:00:00Z",
    "data": { "name": "Ada", "email": "ada@example.com", "message": "Hello!" }
  }
}
```

`submission_id` is `null` when the form does not store submissions. Delivery, signing
(`X-Extatic-Signature`) and retries follow the standard webhook behaviour.
