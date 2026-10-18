# extatic.io — Marketing Website Plan

## Goal

Attract static site developers who need a hosted backend without running a server. Convert visitors into sign-ups by clearly communicating what Extatic does, who it's for, and how easy it is to get started.

---

## Target Audience

- **Primary:** Frontend / Jamstack developers building static sites (Next.js static export, Astro, Hugo, Eleventy, plain HTML) who need to persist user-generated data.
- **Secondary:** Freelancers and agencies building client sites that need a lightweight CMS-like data layer without a full server.
- **Not targeting:** Teams that need a full-stack framework, relational database designers, or engineers already running servers.

---

## Core Value Propositions

1. **No server required** — Add a real backend to any static site in minutes.
2. **Structured data** — Define collections with JSON Schema to keep user data clean.
3. **User auth built in** — Your site users sign in with Google, GitHub, or Facebook; Extatic handles the tokens.
4. **File uploads** — Attach images and documents to any data record, served via CDN.
5. **Webhooks** — React to data changes in external systems automatically.
6. **Team-friendly** — Invite collaborators with role-based access.

---

## Site Structure

```
/                   Home (hero + features overview)
/features           Full feature breakdown
/docs               Developer documentation (or external link)
/pricing            Pricing tiers (placeholder if not yet decided)
/blog               Optional: tutorials and release notes
/login              Redirect to OAuth2 login
/dashboard          Redirect to app dashboard (post-login)
```

---

## Pages

### 1. Home (`/`)

**Objective:** Explain what Extatic is in 5 seconds, show it's easy, prompt sign-up.

#### Sections

**1.1 Hero**
- **Headline:** `The backend for your static site.`
- **Sub-headline:** `Add structured data, user auth, file uploads, and webhooks to any static site — no server required.`
- **CTA buttons:** `Get Started Free` (primary) | `Read the Docs` (secondary)
- **Supporting visual:** Short animated diagram or screenshot — browser → Extatic API → static site.

**1.2 The Problem (1–2 sentences + 3 pain points)**
- Headline: `Static sites shouldn't need a server just to save data.`
- Pain points (icon + short label):
  - "Spinning up a server for a contact form is overkill."
  - "BaaS platforms are either too complex or too expensive."
  - "Form services don't let you query or structure your own data."

**1.3 How It Works (3-step visual)**
1. **Register an App** — Create a project in the Extatic dashboard.
2. **Define a Collection** — Describe your data shape with JSON Schema.
3. **Call the API** — Your static site calls `/api/client/...` to read and write data.

Visual: a minimal code snippet showing a `fetch` call to the client API.

```js
// From your static site
await fetch('https://api.extatic.io/api/client/collections/comments/items', {
  method: 'POST',
  headers: {
    'X-Api-Key': 'YOUR_API_KEY',
    'Authorization': `Bearer ${userToken}`,
    'Content-Type': 'application/json',
  },
  body: JSON.stringify({ author: 'Alice', body: 'Great post!' }),
});
```

**1.4 Feature Highlights (icon grid, 6 cards)**

| Icon | Title | 1-line description |
|------|-------|--------------------|
| Database | Structured Collections | Define your data model with JSON Schema. Invalid data is rejected automatically. |
| Users | AppUser Auth | Let your site visitors sign in with Google, GitHub, or Facebook. |
| Paperclip | File Attachments | Upload images and documents to any record, served via CDN. |
| Bell | Webhooks | Fire HTTP notifications to external services on any data event. |
| Shield | CORS & API Keys | Lock your API to specific origins. Rotate keys any time. |
| Users2 | Team Collaboration | Invite teammates as admins, editors, or viewers. |

**1.5 Testimonials / Social Proof** *(placeholder — fill once available)*
- 3 short quotes from real users.
- Or: "Built for the Jamstack generation" with logos of compatible frameworks (Next.js, Astro, Hugo, Eleventy).

**1.6 CTA Banner**
- Headline: `Ready to add a backend to your static site?`
- Sub: `Free to start. No credit card required.`
- Button: `Create Your Free Account`

**1.7 Footer**
- Links: Home, Features, Docs, Pricing, Blog, Status, GitHub
- Legal: Privacy Policy, Terms of Service
- Copyright: © 2026 Extatic

---

### 2. Features (`/features`)

**Objective:** Deep-dive for developers evaluating Extatic. Each feature section should answer "what does this do and why does it matter."

#### Sections

**2.1 Collections & Schema Validation**
- What: Named buckets for JSON documents. Define a JSON Schema to enforce shape.
- Why: Prevents garbage data from reaching your front end without writing any validation code.
- Visual: Dashboard screenshot of schema editor + a `422` error response example.

**2.2 AppUser Authentication**
- What: Your site visitors sign in via OAuth (Google, GitHub, Facebook). Extatic issues a JWT scoped to your App.
- Why: You don't manage passwords or tokens. One line of code to initiate the OAuth flow.
- Visual: Sequence diagram — User clicks "Sign in with Google" → Extatic OAuth flow → JWT returned to browser.

**2.3 File Attachments**
- What: Upload files to any Item. Configure allowed MIME types, max file size, and max count per Item.
- Why: User-generated media without S3 configuration or a separate CDN account.
- Visual: Config table (defaults: 10 MB, 10 attachments, 1 GB quota) + code snippet for upload.

**2.4 Webhooks**
- What: Subscribe to events (`item.created`, `appuser.created`, etc.). Extatic POSTs a signed payload to your URL.
- Why: Trigger emails, Slack messages, CI pipelines, or serverless functions from data changes.
- Visual: Payload example + signature verification snippet.
- Reliability callout: Exponential backoff retries (1 min → 5 min → 30 min → 2 hr → 12 hr). 7-day delivery logs. Manual re-trigger.

**2.5 Team Collaboration**
- What: Invite collaborators with `admin`, `editor`, or `viewer` roles. Invitation-based — pending until accepted.
- Why: Hand off collection management to a client or content editor without giving away the App key.
- Visual: Role permissions table.

**2.6 Developer Experience**
- RESTful JSON API — no SDK required, works with any language.
- Swagger / OpenAPI docs auto-generated.
- CORS config per App.
- API key rotation.
- Pagination, filtering, and sorting on Item list endpoints.

---

### 3. Docs (`/docs`)

**Objective:** Get a developer to a working integration in under 15 minutes.

> Either hosted on the main site or as a separate docs site (e.g., Docusaurus, Mintlify). Link from the main nav.

#### Suggested top-level sections

1. **Quickstart** — Create an App, define a Collection, post an Item from a plain HTML page.
2. **Authentication** — Platform login (OAuth2 Proxy), AppUser OAuth flow, JWT usage.
3. **Collections** — CRUD, schema definition, attachments configuration.
4. **Items** — CRUD, schema validation errors, filtering and pagination.
5. **Attachments** — Upload flow, size limits, MIME type config, CDN URLs.
6. **Webhooks** — Event types, payload format, signature verification, retry behaviour.
7. **Collaborators** — Invitation flow, role definitions.
8. **API Reference** — Full OpenAPI reference (auto-generated from Swashbuckle).
9. **Error Reference** — Standard error shapes, status codes.

---

### 4. Pricing (`/pricing`)

**Objective:** Remove friction from sign-up. Lead with a free tier.

#### Suggested tier structure *(placeholder — adjust when decided)*

| | Free | Pro | Team |
|---|---|---|---|
| Apps | 1 | 5 | Unlimited |
| Collections per App | 5 | Unlimited | Unlimited |
| Items per App | 1,000 | 50,000 | Unlimited |
| Storage | 100 MB | 5 GB | 50 GB |
| AppUsers | 100 | 5,000 | Unlimited |
| Webhooks | 2 | 20 | Unlimited |
| Team members | 1 | 3 | Unlimited |
| Price | Free | $12/mo | $49/mo |

- FAQ section: "What happens if I exceed limits?", "Can I upgrade/downgrade?", "Is there a trial for Pro?"

---

### 5. Blog (`/blog`) *(optional, phase 2)*

Article ideas for launch:
- "Why your static site needs a backend (and why it doesn't need a server)"
- "Building a comments section with Extatic and Astro"
- "How JSON Schema validation keeps your data clean"
- "Connecting Extatic webhooks to a Slack channel"
- "Setting up user-generated image uploads with Extatic Attachments"

---

## Design Direction

- **Tone:** Developer-first. Direct, technical, no buzzwords.
- **Palette:** Dark theme primary (matches the dashboard). Accent color to match dashboard accent.
- **Typography:** Monospace for code/slugs; clean sans-serif for prose.
- **Code snippets:** Syntax-highlighted, copyable, real examples (not pseudocode).
- **Screenshots:** Dashboard screenshots should be real (taken from the actual running dashboard), not mockups.
- **Animations:** Minimal. A subtle fade-in hero or a short Lottie animation for the "how it works" diagram is fine. Avoid heavy motion.

---

## Tech Stack for the Marketing Site

Options (choose one):
- **Astro** — Static, fast, can import components from React/Angular if needed. Good fit given the Jamstack positioning (dogfood the product).
- **Next.js static export** — Familiar for most devs, good ecosystem.
- **Plain HTML + Tailwind** — Minimal, no framework overhead for a few pages.

Recommendation: **Astro + Tailwind**, deployed to Netlify, Vercel, or Azure Static Web Apps. This lets the Extatic team dogfood their own product for any dynamic content (e.g., blog comments, waitlist signup form).

---

## Launch Checklist

- [ ] OG / Twitter card meta tags on all pages
- [ ] Favicon + brand icons (light and dark variants)
- [ ] robots.txt and sitemap.xml
- [ ] Google Analytics or Plausible (privacy-first)
- [ ] HTTPS certificate (automatic on Netlify/Vercel)
- [ ] 404 page with helpful links
- [ ] Cookie consent (if using analytics)
- [ ] Privacy Policy and Terms of Service pages
- [ ] `status.extatic.io` uptime page link in footer
- [ ] GitHub repo link (if open source)
