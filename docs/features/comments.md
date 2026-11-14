# Comments

Moderated comments for static websites. Module key `comments` (`src/Extatic.Api/Features/Comments`).

## Concepts

- **Page key**: an identifier the site chooses for each page, e.g. `/blog/hello-world` (max 500 chars,
  trimmed, no control characters). Use the same key everywhere for one page.
- **Status**: `pending`, `approved`, `rejected`, `spam`. Only `approved` comments are public.
- **Bodies are plain text.** They are stored exactly as submitted (trimmed) and must always be
  rendered as text (`textContent`, Angular interpolation), never as HTML. Control characters other than
  tab/CR/LF are rejected.
- **Settings** (per App; until first saved the defaults apply and comments are **disabled**):

| Setting | Default | Notes |
|---|---|---|
| `enabled` | `false` | Off → new comments 403, list returns `enabled: false` |
| `moderation_mode` | `all` | `all` (everything pending), `anonymous_only` (signed-in AppUsers auto-approved), `none` |
| `allow_anonymous` | `true` | Off → anonymous POST returns 401 |
| `require_email_for_anonymous` | `true` | |
| `max_length` | 5000 | 1–20000 |
| `max_depth` | 3 | 1–10; top-level comments are depth 1 |
| `auto_close_after_days` | null | Page closes N days after its first (non-spam) comment |
| `blocked_terms` / `blocked_term_action` | `[]` / `pending` | Case-insensitive substring match on body and author name; `pending` or `spam` |
| `max_links` | 2 | More links (`http://`, `https://`, `www.`) → pending |
| `notify_emails` | `[]` | Up to 10 moderators emailed on each new pending/approved comment |
| `expose_avatar_hash` | `false` | Adds `avatar_hash` (SHA-256 of the lower-cased email, Gravatar-compatible) |

Status rules: moderation mode first, then blocked terms (→ pending or spam) and link count (→ pending)
only ever downgrade. Edits by the author re-run the same rules.

## Client API (`/api/{app_public_id}/comments`)

| Method | Route | Auth | Rate limit |
|---|---|---|---|
| GET | `comments?page_key=&page=1&page_size=20&sort=oldest\|newest` | app id; AppUser token optional | ClientReads |
| GET | `comments/counts?page_keys=/a,/b` (max 50) | app id | ClientReads |
| POST | `comments` | app id; AppUser token optional | ClientAnonymousWrites |
| PUT | `comments/{id}` | AppUser token (own comment, ≤ 15 min old) | ClientWrites |
| DELETE | `comments/{id}` | AppUser token (own comment) | ClientWrites |

`GET comments` returns `{ page_key, enabled, is_closed, total, approved_count, page, page_size, items }`.
`total` counts approved **top-level** comments (the paging unit); replies are nested in `replies`
(oldest first) to the configured depth. Each item: `id, parent_id, author_name, avatar_hash, body,
is_deleted, pending, is_own, created_at, edited_at, replies`. A signed-in AppUser also sees their own
pending comments (`pending: true`). Deleted comments that still have replies are tombstones
(`is_deleted: true`, `body`/`author_name` null). **Author emails are never returned by the Client API.**

`POST comments` body: `page_key, page_url?, page_title?, parent_id?, author_name?, author_email?, body,
website`. `website` is a honeypot — leave it empty and hide it from humans; if filled, the API answers
as if the comment were held for moderation but stores it as spam with no notifications.
Anonymous: `author_name` required, `author_email` required unless disabled. Signed-in: name defaults to
the AppUser's display name and the email is taken from the AppUser. Replies need an approved parent on
the same page; depth is limited by `max_depth`. Response `201` with `{ id, parent_id, page_key,
author_name, body, status, created_at, edited_at }` — show "awaiting moderation" when `status` is
`pending`. Closed pages and disabled comments → `403`; validation → `422` with field pointers.

`page_url` is only used as a link in notification emails when its origin is one of the App's
Allowed Origins.

## Platform API (`/api/apps/{app_slug}/comments`)

| Method | Route | Policy |
|---|---|---|
| GET | `comments?status=&page_key=&search=&page=&page_size=` | AppOwnerOrEditor (no status filter → pending first) |
| GET | `comments/{id}` | AppOwnerOrEditor |
| POST | `comments/{id}/approve` · `/reject` · `/spam` | AppOwnerOrEditor |
| POST | `comments/bulk` `{ ids: [...≤100], action: "approve"\|"reject"\|"spam" }` → `{ updated, not_found }` | AppOwnerOrEditor |
| DELETE | `comments/{id}` (hard delete, replies included) | AppOwnerOrAdmin |
| GET / PUT | `comments/settings` (PUT replaces all fields) | AppOwnerOrAdmin |
| GET | `comments/pages?page=&page_size=` | AppAnyAccess |
| POST | `comments/pages/close` · `comments/pages/reopen` `{ page_key }` | AppOwnerOrEditor |
| GET | `comments/stats` → `{ pending, approved, rejected, spam }` | AppAnyAccess |

## Notifications

- Emails (outbox, atomic with the comment): `comments.new` to each `notify_emails` address when a
  comment lands pending or approved; `comments.reply` to the parent's author when a reply becomes
  approved, the parent author is a signed-in AppUser with an email, and is not the replier.
- Webhooks: `comment.created`, `comment.updated`, `comment.deleted`, `comment.approved`,
  `comment.rejected` (also sent when a comment is marked spam; `data.status` tells which). Payloads carry
  the comment without email, IP hash or user agent.

## Embeddable snippet

Minimal, dependency-free widget. It renders everything with `textContent` (never `innerHTML`).

```html
<div id="extatic-comments" data-page-key="/blog/hello-world"></div>
<script>
(function () {
  var API = 'https://api.extatic.example/api/app_XXXXXXXXXXXX/comments'; // your App's public id
  var root = document.getElementById('extatic-comments');
  var pageKey = root.dataset.pageKey;

  function el(tag, text) { var e = document.createElement(tag); if (text != null) e.textContent = text; return e; }

  function renderComment(c) {
    var li = el('li');
    var meta = el('div', c.is_deleted ? '[deleted]' : c.author_name + ' · ' + new Date(c.created_at).toLocaleString());
    meta.style.fontSize = '0.85em';
    li.appendChild(meta);
    if (!c.is_deleted) {
      var body = el('p', c.body);           // plain text, never innerHTML
      body.style.whiteSpace = 'pre-wrap';
      li.appendChild(body);
    }
    if (c.pending) li.appendChild(el('em', 'Awaiting moderation'));
    if (c.replies.length) {
      var ul = el('ul');
      c.replies.forEach(function (r) { ul.appendChild(renderComment(r)); });
      li.appendChild(ul);
    }
    return li;
  }

  function load() {
    fetch(API + '?page_key=' + encodeURIComponent(pageKey))
      .then(function (r) { return r.json(); })
      .then(function (data) {
        root.replaceChildren();
        if (!data.enabled) return;
        var list = el('ul');
        data.items.forEach(function (c) { list.appendChild(renderComment(c)); });
        root.appendChild(el('h3', data.approved_count + ' comment(s)'));
        root.appendChild(list);
        if (!data.is_closed) root.appendChild(form());
      });
  }

  function form() {
    var f = el('form');
    var name = el('input'); name.name = 'author_name'; name.placeholder = 'Name'; name.required = true;
    var email = el('input'); email.name = 'author_email'; email.type = 'email'; email.placeholder = 'Email (not shown)'; email.required = true;
    var body = el('textarea'); body.name = 'body'; body.required = true;
    var trap = el('input'); trap.name = 'website'; trap.tabIndex = -1; trap.autocomplete = 'off';
    trap.style.position = 'absolute'; trap.style.left = '-9999px';  // honeypot: hidden from humans
    var submit = el('button', 'Post comment'); submit.type = 'submit';
    var status = el('p');
    [name, email, body, trap, submit, status].forEach(function (x) { f.appendChild(x); });
    f.addEventListener('submit', function (e) {
      e.preventDefault();
      fetch(API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          page_key: pageKey, page_url: location.href, page_title: document.title,
          author_name: name.value, author_email: email.value, body: body.value, website: trap.value
        })
      }).then(function (r) { return r.json().then(function (j) { return { ok: r.ok, json: j }; }); })
        .then(function (res) {
          if (!res.ok) { status.textContent = res.json.detail || 'Could not post comment.'; return; }
          status.textContent = res.json.status === 'approved' ? 'Posted!' : 'Thanks! Your comment is awaiting moderation.';
          body.value = '';
          if (res.json.status === 'approved') load();
        });
    });
    return f;
  }

  load();
})();
</script>
```

Signed-in AppUsers: send `Authorization: Bearer <appuser-jwt>` on GET/POST (and for PUT/DELETE of
their own comments); omit `author_name`/`author_email`.

## Data

Tables `comments`, `comment_settings` (unique `AppId`), `comment_pages` (unique `AppId, PageKey`).
Comment reply FK (`ParentId`) cascades so a moderator hard delete removes the thread; author deletes keep a
tombstone when replies exist. Row locks (`SELECT … FOR UPDATE`) serialise reply-vs-delete and concurrent
moderation; settings/page rows are created with unique-violation retry.
