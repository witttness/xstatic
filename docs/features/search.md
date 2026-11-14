# Search

Full-text search for static sites, built on PostgreSQL full-text search (`tsvector` + GIN). No
external search engine. Code: `src/Extatic.Api/Features/Search/`, dashboard: `features/search/`.

## Concepts

- **One index per App.** Every document row carries `app_id`; every query filters `app_id` first.
- **Two sources feed it:**
  - **Pages** (`source = "page"`) are pushed by the site's build/CI with the App's secret key.
  - **Items** (`source = "item"`) from collections the tenant opts in. They are indexed automatically
    when an Item is created or updated (in the same transaction as the Item write) and removed when the
    Item is deleted (`ON DELETE CASCADE` on `search_documents.item_id`).
- **Settings** (`search_settings`, created lazily): `enabled` (default **off**, opt-in), `default_language`
  (`english`), `max_documents` (page-document cap, default 10 000; tenants can lower it, the platform
  ceiling is `Search:MaxDocumentsCeiling`), `public_search_enabled` (default on).
- **Collection config** (`search_collection_configs`): `title_pointer` (JSON pointer into `Item.data`),
  `body_pointers[]` (strings or arrays of strings are concatenated), `tags_pointer`, `url_template`
  (`/blog/{slug}`: top-level string/number fields, or `{id}`; values are URL-encoded; if a placeholder
  is missing the hit has no URL), `section`, `language`. Saving a config queues a reindex of that
  collection; disabling or deleting it removes the collection's documents immediately.
- **Reindex jobs** (`search_reindex_jobs`) run in a hosted worker. Jobs are claimed with a conditional
  UPDATE plus a 5-minute lease that is renewed after every batch, so it is safe with several replicas.
  A crashed job is picked up again when its lease expires. At most one *pending* job exists per
  (app, collection | all), so pressing "reindex" twice returns the same job. Reindexing is idempotent:
  each upsert is skipped when the content hash is unchanged.
- **Languages** are Postgres text-search configurations from a fixed allowlist (`simple`, `english`,
  `french`, `german`, `spanish`, … 30 in total). A document is stemmed with its own language, and a
  query is parsed once per language present in the App's index.
- **Ranking**: `ts_rank_cd` with weights title **A** > summary + tags **B** > body **C**, times a
  recency factor of at most 10% (`published_at`, else creation time).
- **Query log** (`search_query_logs`): normalised query (lower-case, max 200 chars) and result count
  for public searches (first page only). It stores no IP addresses and no user ids. Rows older than 30
  days are deleted every hour.

## CI indexing recipe

Use the App's **secret** key (`exk_…`) in `X-Extatic-Secret-Key`. Never ship it to the browser; the
public App id alone is rejected on these endpoints.

```bash
API="https://api.example.com/api/$EXTATIC_APP_ID/search"

# 1. Upsert pages (≤ 500 per request; body ≤ 200 KB of text after HTML stripping)
curl -X PUT "$API/documents" \
  -H "X-Extatic-Secret-Key: $EXTATIC_SECRET_KEY" -H "Content-Type: application/json" \
  -d '{"documents":[{"id":"/blog/hello","url":"/blog/hello","title":"Hello world",
        "body":"<article>…rendered HTML…</article>","summary":"Optional teaser",
        "section":"blog","tags":["intro"],"language":"english",
        "metadata":{"image":"/img/hello.png"},"published_at":"2026-01-01T00:00:00Z"}]}'
# → {"inserted":1,"updated":0,"unchanged":0,"total_page_documents":1}

# 2. After all batches: "these are all my pages" — deletes page documents not listed
curl -X POST "$API/sync" -H "X-Extatic-Secret-Key: $EXTATIC_SECRET_KEY" \
  -H "Content-Type: application/json" -d '{"external_ids":["/blog/hello","/about"]}'

# Delete specific pages / check counts
curl -X DELETE "$API/documents" -H "X-Extatic-Secret-Key: $EXTATIC_SECRET_KEY" \
  -H "Content-Type: application/json" -d '{"external_ids":["/old-page"]}'
curl "$API/documents/stats" -H "X-Extatic-Secret-Key: $EXTATIC_SECRET_KEY"
```

Rules: `id` defaults to `url`; `title` is required; `url` must be a path starting with `/` or an
http(s) URL; up to 50 tags (lower-cased); `metadata` is an object of at most 8 KB and is returned
with hits. HTML in `body` is stripped server-side (tags, comments and `script`/`style`/`noscript`/
`template`/`head` contents removed, entities decoded). Errors are `422` with JSON pointers such as
`/documents/3/body`. A push that would exceed `max_documents` is rejected with `422`; updates to
existing pages are still accepted. Pushing while search is disabled returns `409`. `sync` with an
empty list is rejected (`400`). To clear everything, use `DELETE`.

## Query API (public, App id only)

```
GET /api/{app_public_id}/search?q=&page=1&page_size=10&section=&tags=a,b&source=page|item&collection=slug
GET /api/{app_public_id}/search/suggest?q=gett
```

- `q` is required (empty → `400`), at most 200 characters, parsed with `websearch_to_tsquery`:
  `"exact phrase"`, `-exclude`, `this or that`. Any input is safe; syntax never raises an error.
- `tags` matches all of the listed tags. `page_size` ≤ 50.
- Returns `404` when search is disabled or the public endpoint is turned off.
- Rate limited by `ClientReads` (300/min per App + IP).

```json
{ "query": "kubernetes", "total": 2, "page": 1, "page_size": 10,
  "items": [{ "id": "/guides/k8s", "source": "page", "url": "/guides/k8s", "title": "Kubernetes guide",
              "summary": null, "snippet": "… deploy to <mark>kubernetes</mark> with &lt;3 …",
              "section": "docs", "tags": ["ops"], "metadata": {}, "published_at": "…",
              "collection": null, "item_id": null, "score": 0.42 }] }
```

`suggest` returns up to 8 distinct titles whose words start with the typed text. It uses a prefix
`tsquery` on the last word, built only from letters and digits, against an unstemmed
`to_tsvector('simple', title)` generated column.

## Snippet rendering safety

`snippet` is produced by `ts_headline` with private-use sentinel characters as highlight markers.
The API HTML-encodes every text segment and then turns only the sentinels into `<mark>`/`</mark>`.
Sentinels are removed from all indexed text, so a document cannot forge them. The result is
escaped text whose **only** markup is `<mark>`. A test checks that a body containing
`<script>`, `<img onerror>`, `&` and quotes comes back escaped.

Even so, **do not bind snippets with `innerHTML`**. Split on the mark tags, decode entities and
append text nodes. The dashboard does this (`SearchSnippetComponent`), and the Integration tab shows a
copy-paste `renderSnippet()` for sites. This keeps the site safe even if a future server bug
regressed.

## Platform API (dashboard)

| Method | Route (`api/apps/{app_slug}/search…`) | Policy |
|---|---|---|
| GET | `` (overview) | AppAnyAccess |
| GET / PUT | `/settings` | AppOwnerOrAdmin |
| GET | `/collections`, `/collections/{slug}` | AppOwnerOrAdmin |
| PUT / DELETE | `/collections/{slug}` | AppOwnerOrAdmin |
| POST | `/reindex` (202, returns job) | AppOwnerOrAdmin |
| GET | `/reindex`, `/reindex/{id}` | AppAnyAccess |
| GET | `/documents?page&page_size&source&section&q` | AppAnyAccess |
| DELETE | `/documents/{id}` | AppOwnerOrEditor |
| GET | `/test?q=…` (same as public query, ignores enabled flags, not logged) | AppAnyAccess |
| GET | `/analytics` (top and zero-result queries, last 30 days) | AppAnyAccess |

## Implementation notes

- `search_vector` is **not** a generated column. Postgres requires generation expressions to be
  immutable, and both `language::regconfig` (a text→regconfig cast depends on `search_path`) and
  `array_to_string(tags, ' ')` are only stable. It is computed in the single upsert statement in
  `SearchIndexWriter`, which is the only writer of `search_documents`. `title_vector` uses a fixed
  `'simple'` config, so it *is* a stored generated column (`HasGeneratedTsVectorColumn`).
- All DDL (GIN indexes, generated column, partial unique index with `NULLS NOT DISTINCT`) is
  expressed through EF model configuration. No hand-written migration SQL is needed.
- The search tables use snake_case column names because they are queried with hand-written SQL.
- Concurrency: page pushes and `sync` lock the App's `search_settings` row (`FOR UPDATE`), so
  `max_documents` holds under parallel pushes. Reindex batches read Items `FOR SHARE` in the same
  transaction as the upsert, so a concurrent Item update can never be overwritten by stale data.
