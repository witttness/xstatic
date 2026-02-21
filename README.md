# Extatic

**A storage backend for static websites.**

Extatic gives your static site a full-featured backend — structured data collections, user authentication, file uploads, and webhooks — all through a simple REST API. No server-side application required.

---

## Why Extatic?

Static site generators and JAMstack architectures are fast, secure, and easy to deploy. But the moment your site needs to store user-generated content — comments, form submissions, profiles, uploads — you're stuck wiring up a custom backend or stitching together multiple SaaS tools.

Extatic fills that gap. Define your data shape with JSON Schema, let your users log in with Google or GitHub, and start storing structured JSON documents and file attachments in minutes.

---

## Key Features

**Structured Data Collections** — Define Collections with optional JSON Schema validation. Every Item submitted by your users is validated against the schema before it's stored.

**Third-Party Authentication** — Your site's end-users (AppUsers) authenticate via OAuth providers like Google, Facebook, and GitHub. No password management on your end.

**File & Media Uploads** — Attach images, documents, and other files to Items. Control which Collections allow attachments and restrict MIME types per Collection.

**Webhooks** — Get notified when things happen. Register webhook URLs to receive signed HTTP POST callbacks for events like `item.created`, `item.updated`, `item.deleted`, and more.

**Team Collaboration** — Invite other developers to manage your App with role-based access (admin, editor, viewer). The owner retains full control.

**CORS-Aware** — Configure allowed origins per App so your API calls work securely from any static hosting environment.

---

## Tech Stack

| Layer | Technology | Details |
|---|---|---|
| **Backend API** | .NET 10 (C#) | ASP.NET Core Web API, built on the `mcr.microsoft.com/dotnet/aspnet:10.0` base image |
| **ORM / Data Access** | Entity Framework Core | Code-first migrations with Npgsql PostgreSQL provider |
| **Platform Auth** | OAuth2 Proxy | Sidecar container handling OAuth 2.0 / OIDC for dashboard Users |
| **AppUser Auth** | OAuth 2.0 / OIDC + JWT | API-managed flows for static site end-users |
| **JSON Schema Validation** | JsonSchema.Net | Validates Item payloads against Collection schemas |
| **File Storage** | Azure Blob Storage | Stores Attachment files, optionally fronted by Azure CDN |
| **Background Jobs** | Hangfire or .NET BackgroundService | Webhook delivery, retries, orphaned storage cleanup |
| **Frontend** | Angular | Single-page application for the Extatic dashboard |
| **UI Styling** | Tailwind CSS | Utility-first CSS framework |
| **Frontend State** | NgRx (optional) | Reactive state management for complex UI flows |
| **Database** | PostgreSQL | Relational store with native JSONB column support |
| **Hosting** | Azure Container Apps | Containerized deployment for API, OAuth2 Proxy, and dashboard |
| **Secrets** | Azure Key Vault | Production secrets management |

### Repository Structure

```
extatic/
├── src/
│   ├── Extatic.Api/              # ASP.NET Core Web API project (.NET 10)
│   │   ├── Controllers/          # API controllers (Platform + Client)
│   │   ├── Services/             # Business logic services
│   │   ├── Models/               # Entity models and DTOs
│   │   ├── Data/                 # DbContext, migrations, configuration
│   │   ├── Middleware/           # CORS, rate limiting, error handling
│   │   ├── Auth/                 # OAuth2 Proxy header parsing, AppUser JWT
│   │   ├── Validation/           # JSON Schema validation logic
│   │   ├── Webhooks/             # Webhook dispatch and retry logic
│   │   ├── Storage/              # Azure Blob Storage integration
│   │   ├── Dockerfile
│   │   └── Program.cs
│   │
│   └── extatic-dashboard/        # Angular frontend project
│       ├── src/
│       │   ├── app/
│       │   │   ├── core/         # Auth guards, interceptors, services
│       │   │   ├── shared/       # Shared components, pipes, directives
│       │   │   ├── features/
│       │   │   │   ├── apps/     # App CRUD, settings, API key management
│       │   │   │   ├── collections/  # Collection management, schema editor
│       │   │   │   ├── items/    # Item browsing and detail views
│       │   │   │   ├── appusers/ # AppUser listing and details
│       │   │   │   ├── webhooks/ # Webhook config, delivery logs
│       │   │   │   └── team/     # Collaborator invitations and roles
│       │   │   └── app.routes.ts
│       │   ├── assets/
│       │   ├── styles.css        # Tailwind CSS imports
│       │   └── index.html
│       ├── Dockerfile
│       ├── tailwind.config.js
│       ├── angular.json
│       ├── package.json
│       └── yarn.lock
│
├── infra/
│   ├── oauth2-proxy/             # OAuth2 Proxy configuration
│   │   └── oauth2-proxy.cfg
│   ├── bicep/ or terraform/      # Azure infrastructure-as-code
│   └── azure-container-apps.yml  # Container Apps deployment manifest
│
├── tests/
│   ├── Extatic.Api.Tests/        # Unit and integration tests (.NET)
│   └── extatic-dashboard-e2e/    # E2E tests (Cypress or Playwright)
│
├── docs/
│   └── REQUIREMENTS.md
├── README.md
├── docker-compose.yml            # Local dev: API + OAuth2 Proxy + PostgreSQL + Azurite
└── Extatic.sln
```

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (runtime base image: `mcr.microsoft.com/dotnet/aspnet:10.0`)
- [Node.js 20+](https://nodejs.org/) and [Yarn](https://yarnpkg.com/) (`npm install -g yarn`)
- [Angular CLI](https://angular.dev/tools/cli) (`yarn global add @angular/cli`)
- [PostgreSQL 15+](https://www.postgresql.org/)
- [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) (local Azure Blob Storage emulator)
- [OAuth2 Proxy](https://oauth2-proxy.github.io/oauth2-proxy/) (runs as a sidecar; included in `docker-compose`)
- [Docker](https://www.docker.com/) (recommended for local development via `docker-compose`)

---

## Getting Started

### Option A: Docker Compose (recommended for local dev)

```bash
git clone https://github.com/your-org/extatic.git
cd extatic
docker-compose up -d
```

This starts the API (.NET 10), OAuth2 Proxy, PostgreSQL, and Azurite (Azure Blob Storage emulator). The dashboard dev server can then be started separately:

```bash
cd src/extatic-dashboard
yarn install
ng serve
```

The dashboard is available at `http://localhost:4200` and the API at `http://localhost:5001`. OAuth2 Proxy handles login at `http://localhost:4180`.

### Option B: Manual setup

**Backend:**

```bash
cd src/Extatic.Api

# Update the connection string and Blob Storage settings in appsettings.Development.json
# Then run migrations and start the API:
dotnet ef database update
dotnet run
```

> **Note:** You'll also need OAuth2 Proxy running locally. See `infra/oauth2-proxy/oauth2-proxy.cfg` for configuration.

**Frontend:**

```bash
cd src/extatic-dashboard
yarn install
ng serve
```

### Deploying to Azure

The application is designed to run on **Azure Container Apps**:

1. Build and push container images for the API and dashboard to an Azure Container Registry (or GitHub Container Registry).
2. Deploy using the manifest in `infra/azure-container-apps.yml` or the Bicep/Terraform templates in `infra/`.
3. Provision an **Azure Database for PostgreSQL (Flexible Server)** for the production database.
4. Create an **Azure Blob Storage** account for Attachments, optionally enabling **Azure CDN**.
5. Store secrets (database credentials, OAuth client secrets) in **Azure Key Vault**.
6. Configure OAuth2 Proxy as a sidecar container within the Container App.

### Running Tests

```bash
# .NET unit and integration tests
dotnet test tests/Extatic.Api.Tests

# Angular unit tests
cd src/extatic-dashboard
ng test

# E2E tests
cd tests/extatic-dashboard-e2e
npx playwright test   # or npx cypress run
```

---

## Architecture Overview

```
                    ┌──────────────────────────────────────────────┐
                    │          Azure Container Apps                 │
                    │                                              │
┌──────────┐       │  ┌──────────────┐    ┌──────────────────┐    │
│  Browser  │──────►│  │ OAuth2 Proxy │───►│  Extatic API     │    │
│ (Angular  │       │  │  (sidecar)   │    │  (.NET 10)       │    │
│ Dashboard)│       │  └──────────────┘    └────────┬─────────┘    │
└──────────┘       │                                │              │
                    └────────────────────────────────┼──────────────┘
                                                     │
                         ┌───────────────────────────┼───────────────┐
                         │                           │               │
                         ▼                           ▼               ▼
               ┌──────────────────┐    ┌──────────────────┐  ┌────────────┐
               │ Azure Database   │    │ Azure Blob       │  │ Azure Key  │
               │ for PostgreSQL   │    │ Storage + CDN    │  │ Vault      │
               └──────────────────┘    └──────────────────┘  └────────────┘
```

### Entity Model
```

| Entity | Description |
|---|---|
| **User** | A developer who signs up to Extatic and creates Apps. |
| **App** | A project representing a single static website. |
| **Collection** | A named group of Items within an App, optionally schema-validated. |
| **Item** | A JSON document stored in a Collection, owned by an AppUser. |
| **AppUser** | An end-user of the static site who authenticates via a third-party provider. |
| **Collaborator** | A membership record granting another User role-based access to an App. |
| **Webhook** | A subscription that sends HTTP callbacks when events occur in an App. |
| **Attachment** | A binary file (image, PDF, etc.) linked to an Item. |

---

## API at a Glance

Extatic exposes two API surfaces:

### Platform API (for developers / Users)

Manage your Apps, Collections, Collaborators, and Webhooks.

```
POST   /auth/register
POST   /auth/login

GET    /apps
POST   /apps
GET    /apps/:app_slug
PUT    /apps/:app_slug
DELETE /apps/:app_slug

GET    /apps/:app_slug/collections
POST   /apps/:app_slug/collections
GET    /apps/:app_slug/collections/:col_slug
PUT    /apps/:app_slug/collections/:col_slug
DELETE /apps/:app_slug/collections/:col_slug

GET    /apps/:app_slug/appusers

POST   /apps/:app_slug/collaborators
GET    /apps/:app_slug/collaborators
PUT    /apps/:app_slug/collaborators/:id
DELETE /apps/:app_slug/collaborators/:id
POST   /apps/:app_slug/collaborators/accept

GET    /apps/:app_slug/webhooks
POST   /apps/:app_slug/webhooks
GET    /apps/:app_slug/webhooks/:id
PUT    /apps/:app_slug/webhooks/:id
DELETE /apps/:app_slug/webhooks/:id
GET    /apps/:app_slug/webhooks/:id/logs
```

### Client API (for static sites / AppUsers)

Called from your frontend. Handles authentication, CRUD on Items, and file uploads.

```
POST   /client/auth/:provider
GET    /client/auth/:provider/callback

GET    /client/collections/:col_slug/items
POST   /client/collections/:col_slug/items
GET    /client/collections/:col_slug/items/:id
PUT    /client/collections/:col_slug/items/:id
DELETE /client/collections/:col_slug/items/:id

POST   /client/collections/:col_slug/items/:id/attachments
GET    /client/collections/:col_slug/items/:id/attachments
GET    /client/collections/:col_slug/items/:id/attachments/:att_id
DELETE /client/collections/:col_slug/items/:id/attachments/:att_id
```

---

## Quick Start

### 1. Authenticate and create an App

Platform Users authenticate via OAuth2 Proxy (e.g., Google, GitHub, Microsoft). After logging in through the dashboard or being redirected by OAuth2 Proxy, your session is established automatically.

```bash
# Create an App (session cookie set by OAuth2 Proxy)
curl -X POST https://api.extatic.io/apps \
  -H "Content-Type: application/json" \
  -b "cookie-from-oauth2-proxy" \
  -d '{"name": "My Blog", "slug": "my-blog", "allowed_origins": ["https://myblog.com"]}'
```

### 2. Define a Collection with a schema

```bash
curl -X POST https://api.extatic.io/apps/my-blog/collections \
  -H "Content-Type: application/json" \
  -b "cookie-from-oauth2-proxy" \
  -d '{
    "name": "Comments",
    "slug": "comments",
    "attachments_enabled": false,
    "schema": {
      "type": "object",
      "required": ["post_id", "body"],
      "properties": {
        "post_id": { "type": "string" },
        "body": { "type": "string", "maxLength": 2000 }
      },
      "additionalProperties": false
    }
  }'
```

### 3. Submit Items from your static site

```javascript
// In your frontend JavaScript
const response = await fetch("https://api.extatic.io/client/collections/comments/items", {
  method: "POST",
  headers: {
    "Content-Type": "application/json",
    "X-Api-Key": "your-app-api-key",
    "Authorization": "Bearer <appuser-token>"
  },
  body: JSON.stringify({
    data: {
      post_id: "hello-world",
      body: "Great post! Really enjoyed the read."
    }
  })
});
```

---

## JSON Schema Validation

Collections can optionally include a JSON Schema (draft 2020-12). When present, every Item is validated on create and update. Invalid payloads receive a `422 Unprocessable Entity` response with field-level error details.

```json
{
  "error": "validation_failed",
  "details": [
    { "field": "body", "message": "must be at most 2000 characters" }
  ]
}
```

---

## File & Media Uploads

Enable attachments on a per-Collection basis by setting `attachments_enabled: true`. You can also restrict allowed MIME types per Collection.

```bash
# Upload a file to an Item
curl -X POST https://api.extatic.io/client/collections/photos/items/<item-id>/attachments \
  -H "X-Api-Key: your-app-api-key" \
  -H "Authorization: Bearer <appuser-token>" \
  -F "file=@photo.jpg"
```

Default limits (configurable per App):

| Setting | Default |
|---|---|
| Max file size | 10 MB |
| Max attachments per Item | 10 |
| Total storage per App | 1 GB |

---

## Webhooks

Subscribe to events and get HTTP POST callbacks at your URL. Payloads are signed with HMAC-SHA256.

**Supported events:** `item.created`, `item.updated`, `item.deleted`, `appuser.created`, `attachment.created`, `attachment.deleted`

```bash
curl -X POST https://api.extatic.io/apps/my-blog/webhooks \
  -H "Content-Type: application/json" \
  -b "cookie-from-oauth2-proxy" \
  -d '{
    "url": "https://hooks.example.com/extatic",
    "events": ["item.created", "item.deleted"],
    "is_active": true
  }'
```

Failed deliveries are retried with exponential backoff (up to 5 attempts). Delivery logs are retained for 7 days.

---

## Team Collaboration

Invite other Extatic users to help manage your App. Each collaborator is assigned a role:

| Role | Collections | Items | Webhooks | Collaborators | App Settings |
|---|---|---|---|---|---|
| **owner** | Full | Full | Full | Full | Full |
| **admin** | Full | Full | Full | Full | Read-only |
| **editor** | Full | Full | Read-only | Read-only | Read-only |
| **viewer** | Read | Read | Read | Read | Read |

---

## Documentation

For the full project requirements including entity schemas, authorization rules, and non-functional requirements, see [REQUIREMENTS.md](./REQUIREMENTS.md).

---

## Contributing

Contributions are welcome! Please open an issue to discuss your idea before submitting a pull request.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes (`git commit -m 'Add my feature'`)
4. Push to the branch (`git push origin feature/my-feature`)
5. Open a Pull Request

---

## License

This project is licensed under the [MIT License](./LICENSE).