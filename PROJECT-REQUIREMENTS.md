# Extatic — Project Requirements Document

## 1. Overview

Extatic is a web-based application that serves as a storage backend for static websites. It allows developers to define structured data collections and store user-generated content as JSON documents, all without requiring a traditional server-side application. Static site developers create Apps within Extatic, define Collections with optional JSON Schema validation, and end-users of those static sites (AppUsers) submit Items into those Collections via API.

---

## 2. Goals

- Provide a simple, hosted backend for static websites that need to persist structured data.
- Allow developers (Users) to manage Apps and define Collections with schema validation.
- Enable end-users of static sites (AppUsers) to authenticate via third-party identity providers and submit data (Items) to Collections.
- Enforce data integrity through optional JSON Schema validation on Collections.
- Support file and media uploads attached to Items.
- Provide webhook integrations so external systems can react to data changes.
- Enable multi-user collaboration on Apps through role-based team membership.

---

## 3. Core Entities

### 3.1 User

A **User** is a person who registers and logs in to the Extatic platform itself. Users are the developers or administrators who build and manage static sites.

| Attribute | Type | Description |
|---|---|---|
| `id` | UUID | Unique identifier |
| `email` | String | Email address (unique, sourced from OAuth2 Proxy) |
| `name` | String | Display name |
| `external_id` | String | Subject identifier from the identity provider |
| `created_at` | Timestamp | Account creation time |
| `updated_at` | Timestamp | Last modification time |

**Capabilities:**

- Authenticate via OAuth2 Proxy using a supported identity provider.
- Create, update, and delete Apps.
- Define and manage Collections within their Apps.
- View Items submitted by AppUsers.

### 3.2 App

An **App** represents a single static website or project registered within Extatic. Each App is created by a User and can be shared with additional Users via Collaborators.

| Attribute | Type | Description |
|---|---|---|
| `id` | UUID | Unique identifier |
| `owner_id` | UUID | Foreign key to the User who created the App |
| `name` | String | Human-readable name |
| `slug` | String | URL-friendly unique identifier |
| `api_key` | String | Secret key for API access |
| `allowed_origins` | String[] | CORS whitelist of allowed origins |
| `created_at` | Timestamp | Creation time |
| `updated_at` | Timestamp | Last modification time |

**Relationships:**

- Owned by one **User** (the creator).
- Has zero or more **Collaborators** (other Users with designated roles).
- Has zero or more **Collections**.
- Has zero or more **AppUsers**.
- Has zero or more **Webhooks**.

### 3.3 AppUser

An **AppUser** is an end-user of a static website powered by Extatic. AppUsers authenticate via a third-party identity provider (e.g., Google, Facebook, GitHub, etc.) and are scoped to a specific App.

| Attribute | Type | Description |
|---|---|---|
| `id` | UUID | Unique identifier |
| `app_id` | UUID | Foreign key to the parent App |
| `provider` | String | Identity provider name (e.g., `google`, `facebook`, `github`) |
| `provider_user_id` | String | Unique user ID from the identity provider |
| `email` | String | Email address from the provider (if available) |
| `display_name` | String | Display name from the provider |
| `avatar_url` | String | Profile image URL (optional) |
| `metadata` | JSON | Additional provider-specific profile data |
| `created_at` | Timestamp | First authentication time |
| `last_login_at` | Timestamp | Most recent authentication time |

**Relationships:**

- Belongs to one **App**.
- Has zero or more **Items** across the App's Collections.

**Notes:**

- An AppUser is uniquely identified within an App by the combination of `provider` and `provider_user_id`.
- AppUsers are created automatically upon first successful authentication.

### 3.4 Collection

A **Collection** is a named grouping of Items within an App. It defines the shape of data that Items must conform to.

| Attribute | Type | Description |
|---|---|---|
| `id` | UUID | Unique identifier |
| `app_id` | UUID | Foreign key to the parent App |
| `name` | String | Human-readable name |
| `slug` | String | URL-friendly identifier (unique within App) |
| `schema` | JSON | Optional JSON Schema specification |
| `attachments_enabled` | Boolean | Whether Items in this Collection may have Attachments (default: `false`) |
| `allowed_attachment_types` | String[] | Allowed MIME types for Attachments (only applies when `attachments_enabled` is `true`; empty means all types allowed) |
| `created_at` | Timestamp | Creation time |
| `updated_at` | Timestamp | Last modification time |

**Relationships:**

- Belongs to one **App**.
- Has zero or more **Items**.

**Notes:**

- When `schema` is defined, all Items submitted to the Collection must validate against it.
- When `schema` is `null`, any valid JSON document is accepted as an Item.
- Schema updates should not retroactively invalidate existing Items but should apply to all new and updated Items going forward.
- When `attachments_enabled` is `false` (the default), any attempt to upload an Attachment to an Item in this Collection is rejected with a `403 Forbidden` response.
- When `attachments_enabled` is `true`, Attachments are permitted and optionally restricted by the `allowed_attachment_types` list. If `allowed_attachment_types` is empty, all MIME types are accepted.
- Disabling `attachments_enabled` on a Collection that already has Items with Attachments does not delete existing Attachments, but prevents new uploads.

### 3.5 Item

An **Item** is a JSON document stored within a Collection, submitted by an AppUser.

| Attribute | Type | Description |
|---|---|---|
| `id` | UUID | Unique identifier |
| `collection_id` | UUID | Foreign key to the parent Collection |
| `app_user_id` | UUID | Foreign key to the AppUser who created/owns the Item |
| `data` | JSON | The JSON document payload |
| `created_at` | Timestamp | Creation time |
| `updated_at` | Timestamp | Last modification time |

**Relationships:**

- Belongs to one **Collection**.
- Belongs to one **AppUser**.
- Has zero or more **Attachments**.

**Notes:**

- If the parent Collection has a `schema`, the `data` field must validate against it on create and update.
- Items are owned by a specific AppUser and by default can only be modified or deleted by that AppUser.

### 3.6 Collaborator

A **Collaborator** represents a membership record linking a User to an App they do not own, granting them a specific role. This enables multi-user App management.

| Attribute | Type | Description |
|---|---|---|
| `id` | UUID | Unique identifier |
| `app_id` | UUID | Foreign key to the App |
| `user_id` | UUID | Foreign key to the invited User |
| `role` | Enum | Role within the App: `admin`, `editor`, `viewer` |
| `invited_by` | UUID | Foreign key to the User who sent the invitation |
| `accepted_at` | Timestamp | When the invitation was accepted (null if pending) |
| `created_at` | Timestamp | Invitation creation time |

**Relationships:**

- Belongs to one **App**.
- Belongs to one **User**.

**Role Definitions:**

- **admin** — Full access: manage Collections, Webhooks, Collaborators, view and manage all Items. Cannot delete the App or transfer ownership.
- **editor** — Can manage Collections and Items but cannot manage Collaborators or Webhooks.
- **viewer** — Read-only access to Collections, Items, and AppUsers.

**Notes:**

- A User can only have one Collaborator record per App.
- The App owner implicitly has full access and does not need a Collaborator record.
- Collaborators are created via invitation; an invited User must accept before gaining access.

### 3.7 Webhook

A **Webhook** allows an App to notify external systems when events occur within the App, such as Item creation, updates, or deletions.

| Attribute | Type | Description |
|---|---|---|
| `id` | UUID | Unique identifier |
| `app_id` | UUID | Foreign key to the parent App |
| `url` | String | The target URL to receive POST requests |
| `secret` | String | Shared secret used to sign payloads (HMAC-SHA256) |
| `events` | String[] | List of event types to subscribe to |
| `is_active` | Boolean | Whether the webhook is currently enabled |
| `created_at` | Timestamp | Creation time |
| `updated_at` | Timestamp | Last modification time |

**Supported Event Types:**

- `item.created` — Fired when a new Item is created in any Collection.
- `item.updated` — Fired when an existing Item is modified.
- `item.deleted` — Fired when an Item is deleted.
- `appuser.created` — Fired when a new AppUser authenticates for the first time.
- `attachment.created` — Fired when a new Attachment is uploaded.
- `attachment.deleted` — Fired when an Attachment is removed.

**Payload Structure:**

Each webhook delivery sends an HTTP POST with a JSON body containing:

- `event` — The event type string.
- `timestamp` — ISO 8601 timestamp of the event.
- `app_id` — The App the event belongs to.
- `data` — The full entity that triggered the event (e.g., the Item or AppUser object).

**Notes:**

- Payloads are signed using HMAC-SHA256 with the webhook's `secret`. The signature is included in the `X-Extatic-Signature` header.
- The system retries failed deliveries (non-2xx responses) with exponential backoff, up to 5 attempts.
- After repeated failures, the webhook is automatically deactivated and the App owner is notified.
- Webhook delivery logs are retained for 7 days and accessible via the dashboard.

### 3.8 Attachment

An **Attachment** is a binary file (image, document, video, etc.) uploaded and associated with a specific Item.

| Attribute | Type | Description |
|---|---|---|
| `id` | UUID | Unique identifier |
| `item_id` | UUID | Foreign key to the parent Item |
| `app_user_id` | UUID | Foreign key to the AppUser who uploaded the file |
| `filename` | String | Original filename |
| `content_type` | String | MIME type (e.g., `image/png`, `application/pdf`) |
| `size_bytes` | Integer | File size in bytes |
| `storage_path` | String | Internal path or key in the object store |
| `url` | String | Publicly accessible URL for the file |
| `created_at` | Timestamp | Upload time |

**Relationships:**

- Belongs to one **Item**.
- Belongs to one **AppUser** (the uploader).

**Notes:**

- Files are stored in **Azure Blob Storage**.
- A configurable maximum file size applies per App (default: 10 MB).
- A configurable maximum number of Attachments per Item applies per App (default: 10).
- Allowed MIME types can be restricted at the Collection level.
- When an Item is deleted, all associated Attachments are also deleted from storage.
- Attachment URLs may be served via **Azure CDN** for performance.

---

## 4. Entity Relationship Summary

```
User (1) ──── owns ──────> (0..*) App
User (1) ──── member of ─> (0..*) Collaborator
App  (1) ──── has ───────> (0..*) Collaborator
App  (1) ──── has ───────> (0..*) Collection
App  (1) ──── has ───────> (0..*) AppUser
App  (1) ──── has ───────> (0..*) Webhook
Collection (1) ── has ───> (0..*) Item
AppUser (1) ─── owns ────> (0..*) Item
Item (1) ────── has ─────> (0..*) Attachment
```

---

## 5. Authentication & Authorization

### 5.1 User Authentication

- User authentication is handled by **OAuth2 Proxy**, deployed as a sidecar container alongside the API in Azure Container Apps.
- OAuth2 Proxy handles the OAuth 2.0 / OpenID Connect flow with the configured identity provider (e.g., Microsoft Entra ID, Google, GitHub) and sets authenticated session headers that the API trusts.
- The API reads identity claims (email, name, subject) from headers injected by OAuth2 Proxy (e.g., `X-Forwarded-User`, `X-Forwarded-Email`) and provisions or matches the corresponding User record.
- Unauthenticated requests to protected Platform API endpoints are redirected to the OAuth2 Proxy login flow.

### 5.2 AppUser Authentication

- AppUsers authenticate via third-party identity providers using OAuth 2.0 / OpenID Connect.
- Supported providers (minimum viable set): Google, Facebook, GitHub.
- AppUser OAuth flows are handled directly by the API (not OAuth2 Proxy), since each App may configure different providers and callback URLs.
- Upon successful authentication, an AppUser JWT token is issued, scoped to the specific App.
- The App's `api_key` must be included in all client-side requests to identify the App.

### 5.3 Authorization Rules

| Action | Authorized Actor |
|---|---|
| Create / manage Apps | Authenticated User (owner) |
| Delete App / transfer ownership | App owner only |
| Create / manage Collections | App owner or Collaborator with `admin` or `editor` role |
| View Items in a Collection | Configurable per Collection (public, AppUser-only, owner-only) |
| Create Items | Authenticated AppUser |
| Update / Delete an Item | AppUser who owns the Item, or App owner / authorized Collaborator |
| Manage Webhooks | App owner or Collaborator with `admin` role |
| Manage Collaborators | App owner or Collaborator with `admin` role |
| Upload / Delete Attachments | AppUser who owns the parent Item, or App owner / authorized Collaborator |
| View App (read-only) | App owner or Collaborator with any role |

---

## 6. API Design

The application should expose a RESTful JSON API. Below is a high-level outline of the expected endpoints.

### 6.1 User / Platform API

| Method | Endpoint | Description |
|---|---|---|
| GET | `/auth/me` | Get the currently authenticated User's profile |
| GET | `/apps` | List the authenticated User's Apps |
| POST | `/apps` | Create a new App |
| GET | `/apps/:app_slug` | Get App details |
| PUT | `/apps/:app_slug` | Update an App |
| DELETE | `/apps/:app_slug` | Delete an App |
| GET | `/apps/:app_slug/collections` | List Collections |
| POST | `/apps/:app_slug/collections` | Create a Collection |
| GET | `/apps/:app_slug/collections/:col_slug` | Get Collection details |
| PUT | `/apps/:app_slug/collections/:col_slug` | Update a Collection (including schema) |
| DELETE | `/apps/:app_slug/collections/:col_slug` | Delete a Collection |
| GET | `/apps/:app_slug/appusers` | List AppUsers |
| POST | `/apps/:app_slug/collaborators` | Invite a Collaborator |
| GET | `/apps/:app_slug/collaborators` | List Collaborators |
| PUT | `/apps/:app_slug/collaborators/:id` | Update a Collaborator's role |
| DELETE | `/apps/:app_slug/collaborators/:id` | Remove a Collaborator |
| POST | `/apps/:app_slug/collaborators/accept` | Accept a Collaborator invitation |
| GET | `/apps/:app_slug/webhooks` | List Webhooks |
| POST | `/apps/:app_slug/webhooks` | Create a Webhook |
| GET | `/apps/:app_slug/webhooks/:id` | Get Webhook details |
| PUT | `/apps/:app_slug/webhooks/:id` | Update a Webhook |
| DELETE | `/apps/:app_slug/webhooks/:id` | Delete a Webhook |
| GET | `/apps/:app_slug/webhooks/:id/logs` | View delivery logs for a Webhook |

### 6.2 Client / AppUser API

These endpoints are called from the static website.

| Method | Endpoint | Description |
|---|---|---|
| POST | `/client/auth/:provider` | Initiate AppUser OAuth flow |
| GET | `/client/auth/:provider/callback` | OAuth callback |
| GET | `/client/collections/:col_slug/items` | List Items in a Collection |
| POST | `/client/collections/:col_slug/items` | Create a new Item |
| GET | `/client/collections/:col_slug/items/:id` | Get a specific Item |
| PUT | `/client/collections/:col_slug/items/:id` | Update an Item |
| DELETE | `/client/collections/:col_slug/items/:id` | Delete an Item |
| POST | `/client/collections/:col_slug/items/:id/attachments` | Upload an Attachment to an Item |
| GET | `/client/collections/:col_slug/items/:id/attachments` | List Attachments for an Item |
| GET | `/client/collections/:col_slug/items/:id/attachments/:att_id` | Get Attachment metadata |
| DELETE | `/client/collections/:col_slug/items/:id/attachments/:att_id` | Delete an Attachment |

---

## 7. JSON Schema Validation

- Collections may optionally define a `schema` field containing a valid JSON Schema (draft 2020-12 or compatible).
- When a schema is present, all incoming `data` payloads for Item creation or update must validate against it.
- Validation errors should return a `422 Unprocessable Entity` response with details about which fields failed validation.
- The platform should provide a schema editor or validation preview in the User dashboard.

---

## 8. Webhooks

### 8.1 Overview

Webhooks enable Apps to push event notifications to external URLs in real time. This allows developers to integrate Extatic with external services such as email providers, analytics platforms, CI/CD pipelines, or custom backends.

### 8.2 Behavior

- Users (App owners or admin Collaborators) register one or more webhook URLs per App.
- Each Webhook subscribes to one or more event types (e.g., `item.created`, `item.updated`, `item.deleted`, `appuser.created`, `attachment.created`, `attachment.deleted`).
- When a subscribed event occurs, the system sends an HTTP POST request to the registered URL with a signed JSON payload.
- The payload signature is computed using HMAC-SHA256 with the Webhook's `secret` and is included in the `X-Extatic-Signature` header so recipients can verify authenticity.

### 8.3 Reliability

- Failed deliveries (non-2xx responses or timeouts) are retried with exponential backoff: 1 min, 5 min, 30 min, 2 hours, 12 hours (5 attempts total).
- After all retries are exhausted, the Webhook is marked as `inactive` and the App owner is notified via email.
- Delivery logs (request/response status, headers, timestamps) are retained for 7 days and accessible through the dashboard and API.
- Users can manually re-trigger a failed delivery from the logs.

---

## 9. File & Media Storage

### 9.1 Overview

Extatic supports binary file uploads (images, documents, videos, etc.) as Attachments linked to Items. This allows static sites to handle user-generated media without a separate file hosting service.

### 9.2 Upload Flow

- AppUsers upload files via a multipart form POST to the Attachment endpoint for a specific Item.
- The server first checks that the parent Item's Collection has `attachments_enabled` set to `true`. If not, the request is rejected with `403 Forbidden`.
- The server then validates file size, MIME type (against the Collection's `allowed_attachment_types`), and Attachment count limits before accepting the upload.
- Accepted files are stored in **Azure Blob Storage**.
- A publicly accessible URL is generated and returned in the Attachment response.

### 9.3 Configuration & Limits

| Setting | Scope | Default |
|---|---|---|
| Attachments enabled | Per Collection | `false` |
| Allowed MIME types | Per Collection | All types (when enabled) |
| Maximum file size | Per App | 10 MB |
| Maximum Attachments per Item | Per App | 10 |
| Total storage quota | Per App | 1 GB |

- App owners can adjust these limits from the dashboard (within platform-wide maximums).
- Uploads that exceed any limit are rejected with a `413 Payload Too Large` or `422 Unprocessable Entity` response.

### 9.4 Lifecycle

- Attachments are deleted from object storage when the parent Item is deleted (cascade delete).
- Individual Attachments can be deleted independently by the owning AppUser or an authorized User.
- Orphaned storage cleanup runs as a periodic background job.

### 9.5 Delivery

- Attachment URLs should be served through **Azure CDN** for global performance.
- Optional support for SAS (Shared Access Signature) URLs for private Attachments with time-limited access.

---

## 10. Multi-User App Management

### 10.1 Overview

Apps can be managed collaboratively by multiple Users through the Collaborator system. The App creator remains the owner with full control, while other Users can be invited with specific roles.

### 10.2 Roles

| Role | Collections | Items | Webhooks | Collaborators | App Settings |
|---|---|---|---|---|---|
| **owner** | Full | Full | Full | Full | Full (incl. delete & transfer) |
| **admin** | Full | Full | Full | Full | Read-only |
| **editor** | Full | Full | Read-only | Read-only | Read-only |
| **viewer** | Read-only | Read-only | Read-only | Read-only | Read-only |

### 10.3 Invitation Flow

1. The App owner or an admin Collaborator invites a User by email.
2. The system creates a Collaborator record with `accepted_at = null` and sends an invitation email.
3. The invited User logs in to Extatic and accepts the invitation.
4. Upon acceptance, `accepted_at` is set and the User gains access to the App according to their role.

### 10.4 Rules

- Each User can hold only one role per App.
- Only the App owner can delete the App or transfer ownership to another User.
- Ownership transfer requires confirmation from both the current and new owner.
- Removing a Collaborator revokes their access immediately.
- Collaborators see shared Apps alongside their own Apps in the dashboard.

---

## 11. Technology Stack

### 11.1 Backend

- **Runtime:** .NET 10 (C#), built on the `mcr.microsoft.com/dotnet/aspnet:10.0` base image
- **Framework:** ASP.NET Core Web API
- **ORM:** Entity Framework Core (code-first migrations) with the Npgsql PostgreSQL provider
- **Authentication (Platform Users):** OAuth2 Proxy deployed as a sidecar container, handling OAuth 2.0 / OpenID Connect flows. The API trusts identity headers set by OAuth2 Proxy.
- **Authentication (AppUsers):** OAuth 2.0 / OpenID Connect flows handled directly by the API, issuing JWT bearer tokens scoped per App
- **JSON Schema Validation:** JsonSchema.Net (or equivalent .NET JSON Schema library, draft 2020-12 compatible)
- **Object Storage:** Azure Blob Storage for Attachment file storage
- **Background Processing:** Hangfire or .NET `BackgroundService` for webhook delivery, retry queues, and orphaned storage cleanup
- **Database:** PostgreSQL with native JSON/JSONB column support for Item `data` and Collection `schema` fields

### 11.2 Frontend

- **Framework:** Angular (latest stable)
- **Styling:** Tailwind CSS (utility-first)
- **State Management:** NgRx or Angular Signals (for complex reactive UI flows)
- **HTTP:** Angular `HttpClient` with interceptors for token injection and error handling
- **Routing:** Angular Router with lazy-loaded feature modules
- **Testing:** Jasmine + Karma for unit tests; Cypress or Playwright for end-to-end tests

### 11.3 Infrastructure & Deployment

- **Container Runtime:** Docker; production images built on the .NET 10 base image
- **Hosting:** Azure Container Apps — the API, OAuth2 Proxy sidecar, and Angular dashboard are deployed as containerized services
- **File Storage:** Azure Blob Storage for Attachments, optionally fronted by Azure CDN
- **Database:** Azure Database for PostgreSQL (Flexible Server) in production; local PostgreSQL via Docker for development
- **Local Development:** `docker-compose` for API + PostgreSQL + Azurite (Azure Blob Storage emulator)
- **CI/CD:** GitHub Actions (or Azure DevOps) for build, test, container image push, and deployment to Azure Container Apps
- **API Documentation:** Swagger / OpenAPI via Swashbuckle, auto-generated from ASP.NET Core controllers
- **Secrets Management:** Azure Key Vault for production secrets (database credentials, OAuth client secrets, API signing keys)

---

## 12. Non-Functional Requirements

### 12.1 Performance

- API response time should be under 200ms for typical CRUD operations.
- Support pagination, filtering, and sorting on Item list endpoints.

### 12.2 Security

- All communication over HTTPS.
- API keys must be kept secret and rotatable.
- CORS enforcement based on the App's `allowed_origins` configuration.
- Rate limiting on all public-facing endpoints.
- Input sanitization and protection against injection attacks.

### 12.3 Scalability

- The system should support multiple Apps per User, each with thousands of AppUsers and hundreds of thousands of Items.
- Database design should support efficient querying and indexing of JSON data.

### 12.4 Reliability

- Automated backups of all stored data.
- Graceful error handling with informative error responses.

---

## 13. Future Considerations

- **Real-time subscriptions:** WebSocket or SSE support for live updates to Collections.
- **Custom roles & permissions:** Granular access control for AppUsers beyond owner-only.
- **Usage analytics:** Dashboard showing API call volume, storage usage, and AppUser activity.