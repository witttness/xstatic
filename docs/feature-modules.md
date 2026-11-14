# Feature modules

Conventions for adding a feature (Forms, Events, Comments, ...) without touching shared files.
**Do not edit** `Program.cs`, `AppDbContext.cs`, `app.routes.ts`, the sidebar, `GlobalExceptionHandler`,
any `.csproj`, or `DtoMappers.cs`. If you think you must, stop and ask the lead.

## Where files go (feature `X`, e.g. `Forms`)

| What | Where |
|---|---|
| Entities, entity configurations, DTOs (+ their mapping extensions), services, controllers, module class | `src/Extatic.Api/Features/X/` — namespace `Extatic.Api.Features.X` |
| DbSets | `src/Extatic.Api/Data/AppDbContext.X.cs` (`public partial class AppDbContext { public DbSet<Foo> Foos => Set<Foo>(); }`) |
| Backend tests | `tests/Extatic.Api.Tests/Features/X/` |
| Dashboard | `src/extatic-dashboard/src/app/features/x/` (routes in the existing `x.routes.ts`) |
| Docs | `docs/features/x.md` |

Style: primary constructors, snake_case JSON (automatic), snake_case table names (`ToTable("form_submissions")`),
`HasDefaultValueSql("gen_random_uuid()")` for Guid keys (the Id is **only set after SaveChanges**),
`IEntityTypeConfiguration<T>` classes (picked up automatically). `CreatedAt`/`UpdatedAt` are stamped by `AppDbContext`.

## Module class

```csharp
namespace Extatic.Api.Features.Forms;
public class FormsModule : IFeatureModule            // Extatic.Api.Modules; public + parameterless ctor
{
    public string Key => "forms";
    public IReadOnlyList<string> WebhookEvents => ["form.submitted"];   // default: []
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<FormService>();
        // services.AddHostedService<...>(); services.AddHttpClient("forms", ...);
    }
}
```
Discovered by reflection (`AddFeatureModules`). Controllers are discovered by MVC. Declared webhook events
join the core `item.created|updated|deleted` in `WebhookEventCatalog` (`GET /api/webhook-events`); webhooks
subscribing to anything else get 422. Emit events with `WebhookDispatcher.StageAsync(db, appId, "form.submitted", data)`
before your `SaveChanges` so delivery is atomic with the write.

## Tenancy (mandatory)

Every entity implements `Extatic.Api.Domain.IAppScoped` (`Guid AppId { get; }`) with an FK to `App`
(`OnDelete(Cascade)`), and every query filters by `AppId`. Child rows carry their own `AppId` too.
`TenantScopingConventionTests` fails otherwise. **Do not extend its allowlist** without lead sign-off.

## Controllers

| Surface | Route | Base class | Auth |
|---|---|---|---|
| Platform (dashboard) | `api/apps/{app_slug}/x/...` | `Controllers.PlatformControllerBase` → `CurrentApp`, `UserId`, `UserEmail` | `PolicyNames.AppAnyAccess` (read) / `AppOwnerOrEditor` / `AppOwnerOrAdmin` / `AppOwnerOnly` |
| Platform admin (global) | `api/admin/...` | `PlatformControllerBase` (`UserId`) | `PolicyNames.PlatformAdmin` (email in `Platform:AdminEmails`) |
| Client (static site) | `api/{app_public_id}/x/...` | `Controllers.ClientControllerBase` → `CurrentApp`, `Guid? AppUserId`, `Guid RequireAppUserId()` (401) | `PolicyNames.ClientAnonymousOrAppUser` (token optional) / `AuthenticatedAppUser` (token required) |
| Server-to-server / CI | `api/{app_public_id}/x/...` | `ClientControllerBase` (`CurrentApp`) | `PolicyNames.AppServerKey` — header `X-Extatic-Secret-Key: exk_...` (the App's secret key) |

`AppUserId` is null for a token issued to another App — never read `extatic:app_user_id` from `User` yourself.
Rate limits (`[EnableRateLimiting(...)]`, `Auth.RateLimitPolicyNames`): `ClientReads` (300/min App+IP),
`ClientWrites` (60/min App+AppUser), `ClientUploads` (2 concurrent), `ClientAnonymousWrites` (10/min App+IP —
use on every unauthenticated public write: form posts, comments, registrations).

Errors: throw from `Services/ServiceExceptions.cs` — `BadRequestException` 400, `UnauthorizedException` 401,
`ForbiddenException` 403, `NotFoundException` 404, `ConflictException` 409, `FileTooLargeException` 413,
`ValidationException(message, [new ValidationError(pointer, message)])` 422, `TooManyRequestsException(message, retryAfter?)` 429.
Paged lists return `Dtos.PagedResult<T>(Items, Total, Page, PageSize)`.

## Shared services

- **AppUser tokens** — `Auth.AppUserTokenService.CreateAccessToken(AppUser user, TimeSpan? lifetime = null)` (singleton; default 30 days).
- **Email** — `Notifications.IEmailQueue.Stage(AppDbContext db, Guid appId, EmailMessage message)` returns the
  `EmailOutboxMessage`; it only `Add`s to `db` — your `SaveChanges` commits it with the business change.
  `EmailMessage(string To, string Subject, string TextBody, string? HtmlBody, string? ReplyTo, string Category)`;
  Category like `"forms.notification"`. Invalid addresses → `ValidationException`; CR/LF stripped from the subject.
  **HTML-encode any user content you put in `HtmlBody`.** `EmailDeliveryWorker` sends as "{App.Name} via Extatic",
  5 attempts (1m, 5m, 30m, 2h, 12h). Local dev: Mailpit UI at http://localhost:8025. No SMTP host → logged only.
- **Tenant secrets** — `Security.ISecretProtector` (`string Protect(string)`, `string Unprotect(string)`; singleton).
  Store only protected values (e.g. OAuth client secrets, payment keys); never return them from the API or log them.
- **Item hook** — implement `Services.IItemChangeListener`
  (`Task OnItemSavedAsync(AppDbContext db, Guid appId, Item item, bool created)`,
  `Task OnItemDeletingAsync(AppDbContext db, Guid appId, Item item)`) and register
  `services.AddScoped<IItemChangeListener, MyListener>()`. Runs inside ItemService's transaction: write via `db`
  only, don't call `SaveChanges`; throwing aborts the item write.
- **Platform admins** — `Auth.IPlatformAdminService.IsAdmin(string? email)`.

## Common utilities (`Extatic.Api.Common`)

- `Slugs.IsValid(string?)`, `Slugs.EnsureValid(string? slug, string pointer = "slug")` — `[a-z0-9-]{1,100}`, else 422.
- `Paging.Validate(int page, int pageSize)` (400 unless page ≥ 1, 1 ≤ size ≤ 100), `Paging.Skip(page, pageSize)`.
- `new CsvBuilder().AddRow(params IEnumerable<object?> cells)` → `ToString()` / `ToBytes()` (UTF-8 BOM). RFC 4180
  quoting; cells starting `= + - @` are prefixed with `'`. Serve with `File(bytes, "text/csv", "x.csv")`.
- `Tokens.NewUrlToken(int bytes = 32)`, `Tokens.Hash(string)` (SHA-256 hex — store hashes, not tokens), `Tokens.FixedTimeEquals(a, b)`.
- `ClientIps.Hash(HttpContext)` / `ClientIps.Hash(IPAddress?, string salt)` — never store raw IPs.

## Tests (`tests/Extatic.Api.Tests`)

Use `IClassFixture<ApiTestFixture>` (real Postgres; `SeedAppAsync()`, `SeedAppUserAsync(appId)`,
`SeedCollaboratorAsync(...)`, `CreateContext()`, `CreateScopeFactory()`). Helpers in `TestSupport/`:
- `TestContexts.Platform(App? app, Guid userId, string? email = null)` and
  `TestContexts.Client(App app, Guid? appUserId = null, IPAddress? remoteIp = null, Guid? tokenAppId = null)` →
  `ControllerContext` for `controller.ControllerContext = ...`.
- `TestServices.WebhookDispatcher(fixture)`, `.EmailQueue()`, `.SecretProtector()`, `.AppUserTokenService()`,
  `.PlatformAdmins(params string[] emails)`, `.Configuration(overrides)`; `FakeEmailSender(failFirst)` records sends.
- `new TestAuthPipeline(fixture)` → `CreateContext(path, headers)` + `AuthorizeAsync(context, PolicyNames.X)` runs
  the real schemes/policies (use to prove your endpoints' auth).
Cover at least: cross-tenant access is denied/404, anonymous vs signed-in behaviour, validation errors.

### Migrations — do NOT commit any

The lead generates one combined migration after merging. To run tests locally:
```bash
dotnet ef migrations add TmpX --project src/Extatic.Api     # throwaway
dotnet test tests/Extatic.Api.Tests
git checkout -- src/Extatic.Api/Migrations && git clean -fdq src/Extatic.Api/Migrations
```

## Dashboard

- Routes: add to your `features/x/x.routes.ts` (`FORMS_ROUTES`, `EVENTS_ROUTES`, `COMMENTS_ROUTES`,
  `AUTHENTICATION_ROUTES`, `MEMBERS_ROUTES`, `DONATIONS_ROUTES`, `SEARCH_ROUTES`, `SCHEDULING_ROUTES`,
  `SCHEMA_LIBRARY_ROUTES`) — already spread into `app.routes.ts`. Paths are shell children:
  `apps/:appSlug/x`, `apps/:appSlug/x/new`, `apps/:appSlug/x/:id` (Schema Library: `admin/schemas...` with
  `canActivate: [platformAdminGuard]`). Route params bind to `@Input()`s (`appSlug`, `id`).
- The sidebar already links `apps/:appSlug/<x>` and (admins) `/admin/schemas`.
- `ApiService` (paths are relative to `/api`): `get<T>(path, query?)`, `post`, `put`, `patch`, `delete`,
  `getBlob(path, query?)` for CSV downloads. `PagedResult<T>` in `core/models/paged.model.ts`.
- Standalone components + signals + Tailwind tokens used by existing screens (`bg-surface`, `text-text-muted`,
  `border-border`, `text-error`, `app-page-header`, `app-button`, `app-empty-state`, `app-confirm-modal`).
- Verify: `npx ng build` and `npx ng test --watch=false` (Vitest).
