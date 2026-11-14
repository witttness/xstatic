# Authentication (email + password for AppUsers)

End users of a static site register and sign in with email + password, per App (tenant). Accounts are ordinary
`AppUser` rows with `provider = "password"` and `provider_user_id = <lowercase email>`, so the same email in two
Apps is two unrelated users. Access tokens come from `AppUserTokenService.CreateAccessToken`, so every existing
`AuthenticatedAppUser` endpoint (items, attachments, ...) accepts them unchanged. Social OAuth is out of scope.

Code: `src/Extatic.Api/Features/Authentication/`. Dashboard: `features/authentication` (settings + integration
snippets) and the Users list (verified / disabled badges, enable / disable).

## Settings (`auth_settings`, one row per App, defaults until first save)

| Field | Default | Rule |
|---|---|---|
| `registration_enabled` | true | false → `POST register` is 403 |
| `require_email_verification` | true | login is 403 `email_not_verified` until verified |
| `password_min_length` | 10 | 8–128 |
| `access_token_minutes` | 60 | 5–1440 |
| `refresh_token_days` | 30 | 1–365 (sliding: each rotation gets a new full lifetime) |
| `max_failed_attempts` / `lockout_minutes` | 5 / 15 | 1–100 / 1–1440 |
| `verify_url_template`, `reset_url_template` | null | absolute `https` URL (or `http` on localhost/127.0.0.1), contains `{token}`, no credentials; if the App has allowed origins, the URL's origin must be one of them (localhost always allowed). Null → the email contains the raw token as a code. |
| `allowed_email_domains` | [] | lowercase domains; exact match on the part after `@`; empty → any |

## Client API — `api/{app_public_id}/auth/...`

Unauthenticated POSTs use the `ClientAnonymousOrAppUser` policy and the `ClientAnonymousWrites` rate limit
(10/min per App + IP). Signed-in endpoints use `AuthenticatedAppUser` (+ `ClientWrites` / `ClientReads`).

| Method | Route | Auth | Body → Response |
|---|---|---|---|
| POST | `register` | anon | `{email, password, display_name?}` → **202** `{message}` (always the same) |
| POST | `verify-email` | anon | `{token}` → 200 `{email_verified: true}`; bad/used/expired → 400 |
| POST | `resend-verification` | anon | `{email}` → **202** generic |
| POST | `login` | anon | `{email, password}` → 200 `{access_token, token_type, expires_in, refresh_token, user}` |
| POST | `refresh` | anon | `{refresh_token}` → 200 same shape (new refresh token) |
| POST | `logout` | anon | `{refresh_token}` → 204 (revokes that session's family; unknown tokens ignored) |
| POST | `logout-all` | AppUser | → 204 (revokes every refresh token of the user) |
| POST | `forgot-password` | anon | `{email}` → **202** generic |
| POST | `reset-password` | anon | `{token, password}` → 204 |
| GET | `me` | AppUser | → `{id, email, display_name, avatar_url, metadata, email_verified, last_login_at, created_at}` |
| PUT | `me` | AppUser | `{display_name, avatar_url, metadata}` — replaces all three (null clears). avatar https only (≤ 2048), metadata a JSON object ≤ 8 KB |
| POST | `change-password` | AppUser | `{current_password, new_password}` → 200 tokens (new session; all others revoked) |
| DELETE | `me` | AppUser | `{password}` → 204; deletes the AppUser (credential + tokens cascade; Items/Attachments keep `app_user_id = null`) |

Errors: 401 generic `Invalid email or password` (unknown email, wrong password, disabled); 429 + `Retry-After`
while locked out; 403 with `"code": "email_not_verified"` when the password is right but the email is unverified;
422 field errors (`pointer` = `email`, `password`, `new_password`, `current_password`, ...). A wrong
`current_password` (change-password / delete) is 422 and counts toward lockout.

Platform API — `api/apps/{app_slug}/auth/...`, all `AppOwnerOrAdmin`:
`GET|PUT settings`, `POST users/{id}/disable|enable` (→ AppUserDto), `POST users/{id}/revoke-sessions` (204),
`POST users/{id}/send-password-reset` (202; 400 if the user has no password). Users of another App → 404.

Webhook events: `appuser.registered`, `appuser.verified`, `appuser.login`, `appuser.deleted`
(data: `app_user_id, email, display_name, provider, email_verified`), staged atomically with the change.

## Flows

1. **Register** → user + credential (+ 24 h verification token and email when verification is required).
2. **Verify** → `verify-email {token}`; tokens are single-use. `resend-verification` replaces outstanding tokens.
3. **Login** → access token (`access_token_minutes`) + refresh token (new family).
4. **Refresh** → the presented token is atomically marked used and replaced; presenting a used token again
   (or racing two refreshes with one token) revokes the whole family — the legitimate client must log in again.
5. **Forgot / reset** → 1 h single-use token; requesting a new one invalidates the previous; a successful reset
   revokes all refresh tokens, clears the lockout and marks the email verified (the link proved ownership).

## Security notes

- **Storage:** passwords use ASP.NET Core Identity's `PasswordHasher` (PBKDF2; rehashed on login when the
  hasher asks). Verification / reset / refresh tokens are 256-bit random and stored only as SHA-256
  (`Tokens.Hash`); request IPs only as `ClientIps.Hash`.
- **Enumeration:** `register`, `resend-verification` and `forgot-password` return the same 202 body whether or
  not the account exists. Registering an existing email emails the owner "you already have an account" instead.
  Login always runs one hash verification (a dummy hash for unknown users) so timing is uniform. Known residual
  signal: after `max_failed_attempts` wrong passwords an existing account answers 429 while an unknown email keeps
  answering 401; the per-IP rate limit bounds how fast this can be probed.
- **Disable vs access-token lifetime:** disabling revokes all refresh tokens and blocks login, refresh and the
  `auth/me*` endpoints immediately, but an access token already issued is a stateless JWT and remains valid on
  other `AuthenticatedAppUser` endpoints for up to `access_token_minutes`. Keep that value short.
- **Token storage on a static site:** keep the access token in memory only. The refresh token must survive
  reloads, so `sessionStorage` (per tab) or `localStorage` (persistent) are the practical options; both are
  readable by any script on the page, so a strict Content-Security-Policy and no untrusted third-party scripts
  matter. Rotation + reuse detection limits the damage of a stolen refresh token. Never put tokens in URLs other
  than the one-off verify/reset links.
- **Tenancy:** every lookup filters by AppId; a token or credential from App A never resolves on App B.
- **Cleanup:** `AuthTokenCleanupService` (hourly) deletes tokens that expired, were revoked, or (non-refresh)
  were used more than 7 days ago. Used refresh tokens are kept until expiry so reuse is still detected.
