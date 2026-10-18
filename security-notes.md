# Extatic Security Notes

## API Key Visibility in Static Sites

**Q: The Extatic API key is not a secret when used in a static website — anyone can pick it out of the site's code. How problematic is that?**

This is a real design issue. The API key is embedded in browser JavaScript and visible in page source, DevTools network tab, and any proxy logs. CORS `allowed_origins` is **not a mitigation** — CORS is browser-only enforcement; curl/Postman/scripts ignore it entirely.

### How bad is it?

Depends on what the key gates. **If writes require an AppUser JWT** (which they do per the design), a stolen API key alone can only:
- Read publicly-accessible collections (intentional)
- Initiate OAuth flows (low risk — requires a real provider account)
- Probe collection structure (minor info leak)

This is the same model as Firebase (`apiKey`) and Supabase (`anon` key) — both are public and embedded in client code. Real security lives in auth rules, not in keeping the identifier secret.

### The genuine design contradiction

Current flow:
1. Create App → receive `api_key`
2. Dashboard says "Copy it now — it will not be shown again" (signals *secret*)
3. Developer embeds it in public JavaScript (makes it permanently public)

Can't rotate it without redeploying every site that uses it.

### Concrete risks that do exist

1. **Rate limit / quota abuse** — anyone with the key can hammer item creation, burning through plan limits (items quota, storage quota, AppUser creation)
2. **Anonymous write abuse** — if unauthenticated item creation is ever supported, the API key provides zero protection against spam
3. **Enumeration** — collection slugs, item structure, AppUser count are discoverable

### The fix: split into two credentials

| Credential | Visibility | Purpose |
|---|---|---|
| **App ID** (publishable key) | Public, embedded in JS | Identifies which App a client request belongs to |
| **Secret key** | Server-only, never in browser | Admin operations, webhook verification, server-side reads |

Client API uses App ID. Platform/server-to-server uses secret key. Secret key is what you show once and never again. App ID is permanent and freely copyable.

### Bottom line

Not catastrophically broken — writes require AppUser OAuth (real protection). Biggest risks are **misleading naming/UX** and **no documented rate limiting** to prevent quota abuse via bulk authenticated AppUser accounts.

---

## JWT Verification for AppUser Tokens

**Q: How can Extatic verify the JWT received from users? Will it have to request the secret on app setup?**

**No external secret needed.** Extatic is the JWT *issuer* — it doesn't pass the OAuth provider's token to the client. It completes the OAuth flow, gets user info, then mints its own JWT with a signing key it already holds.

### Flow

```
AppUser → Extatic: "login with GitHub"
Extatic → GitHub:  OAuth dance (code exchange)
GitHub  → Extatic: user profile
Extatic → AppUser: Extatic-signed JWT  ← Extatic minted this
AppUser → Extatic: Bearer <that JWT>
Extatic:           verify with own key  ← already has it
```

### JWT claims (minimum)

```json
{
  "sub":    "appuser-uuid",
  "app_id": "my-blog",
  "email":  "alice@example.com",
  "iat":    1741651200,
  "exp":    1741737600
}
```

**Critical:** `app_id` claim must be validated against the `X-Api-Key` on each request — otherwise a token issued for App A would be valid on App B.

### Signing key options

- **One global EC keypair (ES256)** — simplest; must enforce `app_id` claim rigorously
- **Per-app signing keys** — better isolation; more key management overhead; probably overkill at this stage

**Recommendation:** Start with one global ES256 keypair, enforce `app_id` validation, revisit per-app keys if needed.

### The one secret that IS needed on app setup

If developers configure their own OAuth provider apps (own Google/GitHub credentials with custom callback URLs), they provide `client_id` + `client_secret` per provider per app. Extatic stores those encrypted (Azure Key Vault in prod).

If Extatic runs shared OAuth apps (one Google OAuth app for all of Extatic), no per-app secrets needed — but developers lose branding control over the OAuth consent screen.

### Summary

| Concern | Requires developer-provided secret? |
|---|---|
| Extatic verifying its own JWTs | No — self-contained |
| Extatic completing OAuth with providers | Only if per-app OAuth credentials are supported |
| AppUser reading JWT claims on the client | No — JWT is signed, not encrypted |
