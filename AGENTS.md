# AGENTS.md

This file provides guidance for agentic coding agents working in this repository.

## Project Overview

Extatic is a storage backend for static websites providing structured data collections, user auth, file uploads, and webhooks via REST API. The repository contains:
- **Backend**: .NET 10 (C#) ASP.NET Core Web API (`src/Extatic.Api`)
- **Frontend**: Angular 21 with Tailwind CSS (`src/extatic-dashboard`)
- **Tests**: xUnit for .NET (`tests/Extatic.Api.Tests`)

---

## Build / Lint / Test Commands

### Backend (.NET)

```bash
# Navigate to API project
cd src/Extatic.Api

# Apply EF Core migrations
dotnet ef database update

# Run the API
dotnet run

# Run all tests
dotnet test tests/Extatic.Api.Tests

# Run a single test (by name pattern)
dotnet test tests/Extatic.Api.Tests --filter "Name~CreateApp"

# Run tests with verbose output
dotnet test tests/Extatic.Api.Tests -v n

# Build the project
dotnet build
```

### Frontend (Angular)

```bash
# Navigate to dashboard
cd src/extatic-dashboard

# Install dependencies (first time)
yarn install

# Start dev server
yarn start         # or: ng serve
# Access at http://localhost:4200

# Run unit tests
yarn test          # or: ng test (uses Vitest)

# Build for production
yarn build         # or: ng build
```

### Docker Compose (full stack)

```bash
# Start all services (API, OAuth2 Proxy, PostgreSQL, Azurite)
docker-compose up -d

# API: http://localhost:5001
# Dashboard: http://localhost:4200
# OAuth2 Proxy: http://localhost:4180
```

---

## Code Style Guidelines

### C# (.NET)

**Formatting**
- Use file-scoped namespaces (`namespace Extatic.Api.Services;`)
- Use primary constructors (`public class AppService(AppDbContext db)`)
- 4-space indentation (Visual Studio default)
- Open braces on same line

**Naming Conventions**
- PascalCase for classes, methods, properties, enums
- camelCase for local variables, parameters
- Prefix interfaces with `I` (e.g., `IBlobStorageService`)
- Use meaningful, descriptive names

**Types**
- Enable nullable reference types (`<Nullable>enable</Nullable>`)
- Use `var` for implicit typing when type is obvious
- Prefer concrete types over interfaces in simple services
- Use `string` for strings, `int`/`Guid` for IDs, `bool` for flags

**Imports**
- Group imports: System → External → Internal
- Use file-scoped namespace to reduce boilerplate
- No unused imports

**Error Handling**
- Throw custom exceptions: `NotFoundException`, `ConflictException`, `ForbiddenException`
- Use `ActionResult<T>` return types in controllers
- Catch specific exceptions in services; let generic errors bubble
- Return appropriate HTTP status codes (404, 409, 403, 422)

**Controller Patterns**
- Constructor injection of services
- Extract user context from `User.Claims` or `HttpContext.Items`
- Use `[FromRoute]`, `[FromBody]` attributes explicitly
- Follow RESTful conventions in route naming

### TypeScript / Angular

**Formatting**
- 2-space indentation (per `.editorconfig`)
- Single quotes for strings
- Trailing commas in arrays/objects
- Use ESLint/Prettier if available

**Naming**
- PascalCase for component/interface/class names
- camelCase for methods, properties, variables
- Prefix components with feature context (e.g., `AppsListComponent`)
- Suffix services with `Service` (e.g., `ApiService`)

**Angular Patterns**
- Use standalone components (`standalone: true`)
- Prefer signals (`signal()`, `computed()`, `effect()`) over RxJS subjects
- Use `inject()` for dependency injection
- Use new control flow syntax (`@if`, `@for`, `@switch`)
- Import components directly in `imports` array

**Imports**
- Use relative paths for same-feature imports
- Use path aliases (`@core/`, `@shared/`) for cross-feature imports
- Group: Angular → External → Internal

**Templates**
- Inline templates for simple components
- Use semantic HTML with Tailwind classes
- Avoid complex logic in templates; move to component

**State Management**
- Use Angular signals for local component state
- Use services with signals for shared state
- Keep components focused on presentation

---

## Project Structure

```
src/
├── Extatic.Api/
│   ├── Controllers/
│   │   ├── Platform/     # Dashboard APIs (/apps/*)
│   │   └── Client/       # Client APIs (/client/*)
│   ├── Services/         # Business logic
│   ├── Domain/Entities/  # EF Core entities
│   ├── Dtos/             # Request/Response DTOs
│   ├── Data/             # DbContext, configurations
│   ├── Auth/             # Authentication handlers
│   ├── Validation/       # JSON Schema validation
│   ├── Webhooks/         # Webhook dispatch logic
│   ├── Storage/          # Blob storage integration
│   └── Middleware/       # Custom middleware
│
└── extatic-dashboard/
    └── src/app/
        ├── core/         # Guards, interceptors, services
        ├── shared/       # Reusable components
        └── features/     # Feature modules (apps, collections, etc.)
```

---

## Database

- Use Entity Framework Core code-first migrations
- Run `dotnet ef migrations add <Name>` to create migrations
- Run `dotnet ef database update` to apply migrations
- Store `Item.data` and `Collection.schema` as JSONB columns

---

## API Authentication

- **Platform API**: Authenticated via OAuth2 Proxy headers (`X-Forwarded-User`, `X-Forwarded-Email`)
- **Client API**: Authenticated via `X-Api-Key` + JWT bearer token

---

## Testing Guidelines

- Use xUnit with `Fact` and `Theory` attributes
- Follow AAA pattern: Arrange, Act, Assert
- Use in-memory database for integration tests
- Test service methods directly; test controllers for routing/response codes
- Use descriptive test names: `CreateApp_ReturnsCreated_WhenValidRequest`

---

## Key Files

| File | Purpose |
|------|---------|
| `CLAUDE.md` | Detailed project context for Claude |
| `README.md` | Setup and architecture docs |
| `docker-compose.yml` | Local dev environment |
| `src/Extatic.Api/Program.cs` | API entry point, middleware setup |
| `src/Extatic.Api/Data/AppDbContext.cs` | EF Core DbContext |
