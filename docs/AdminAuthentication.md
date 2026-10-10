# Single-administrator authentication

TACC's static `/admin/` page signs in through `POST /api/admin/login`. Credentials are checked only in Azure Functions. No database, identity provider, Function key in JavaScript, or new Azure service is required.

## Private configuration

Set these three values in the Azure Function App's application settings, or in `api/local.settings.json` under `Values` for development:

| Setting | Value |
| --- | --- |
| `ADMIN_USERNAME` | The one administrator username; case-sensitive, no surrounding whitespace/control characters, at most 128 characters |
| `ADMIN_PASSWORD_HASH` | `v1$pbkdf2-sha256$600000$<base64-salt>$<base64-derived-key>` |
| `ADMIN_SESSION_SECRET` | Base64 encoding of a cryptographically random 32-byte key |

The hash uses PBKDF2-HMAC-SHA256 with 600,000 iterations, a random salt of at least 16 bytes, and a 32-byte derived key. Hash comparison uses constant-time comparison. Wrong usernames still perform the password derivation and receive the same generic error as wrong passwords.

From the repository root, generate credentials offline:

```powershell
powershell.exe -NoProfile -File .\api\Scripts\New-AdminCredentials.ps1 -Username owner
```

PowerShell 7 can run the same script with `pwsh -NoProfile -File ...`. It securely prompts for a unique 16-256-character passphrase and emits the username, encoded hash and new session secret. It never accepts a password as a command-line argument. Copy its output into the private settings manually. Do not redirect it into a tracked file, record it in terminal transcripts, or share it in logs or screenshots.

If local settings do not already exist, copy `api/local.settings.example.json` to `api/local.settings.json`. Replace only the three authentication placeholders in `Values`; preserve your storage and Stripe settings and the existing `Host` section. The real local settings are ignored by Git. Never place these values in `config.js`, a bundle, a publish profile, or a committed example. No real credentials are provided in this repository.

Authentication settings are validated when the API initializes. Missing/invalid configuration logs a generic configuration error and returns 503 on login/admin inventory requests; public inventory, health, checkout and signed webhooks continue independently. Fixing settings requires the normal Function App restart/configuration reload. Existing storage configuration is still required for the API to run.

## Login and protected routes

`POST /api/admin/login` accepts a JSON object containing `username` and `password`. Requests are limited to 4 KiB, usernames to 128 characters and passwords to 256 characters. Successful responses contain `accessToken` and `expiresAt` (UTC ISO-8601). Error bodies use the existing `{ "error": "..." }` contract:

| Result | HTTP status |
| --- | --- |
| Successful login | 200 |
| Malformed/missing/oversized credentials | 400 |
| Wrong username or password | 401, generic message |
| Login limit exceeded | 429, `Retry-After` seconds |
| Missing/invalid authentication settings | 503 |

Tokens are signed locally with HS256 and contain the administrator username as `sub`. Issuer is fixed to `tacc-admin`, audience to `tacc-inventory-api`, and expiration is exactly eight hours after issue (JWT timestamps use whole UTC seconds). The API validates signature, algorithm, issuer, audience, lifetime and administrator identity, with zero expiration clock skew. There are no refresh tokens.

Both `GET /api/admin/inventory/{productId}` and `PUT /api/admin/inventory/{productId}` require exactly one `Authorization: Bearer <token>` header. Their explicit middleware mapping is separate from login, public inventory, health, Stripe checkout and the webhook. Missing or invalid tokens return 401; missing request data fails closed. Authenticated identity is retained as `AdminPrincipal` for the existing inventory audit log. When adding or renaming an admin function, update the explicit mapping and its route-coverage tests.

Login and admin responses use `Cache-Control: no-store`. Credentials and tokens are never logged by the authentication implementation. Keep request-body/header capture disabled in any additional diagnostics.

The browser stores the session in `sessionStorage`, restores an unexpired session on reload, and attaches the token only to the configured API. API 401, expiry and logout clear the stored session. The password field is cleared after login attempts. Bearer tokens never appear in query parameters, and fetches omit cookies and reject redirects.

Session storage remains accessible to same-origin JavaScript: XSS can steal a token. Logout removes the browser copy; a copied token remains usable until expiration or session-secret rotation. There is no per-token server revocation list.

## Basic throttling and client IP

Each worker permits five login attempts per source per one-minute window and at most two simultaneous login checks. Limiting happens before request parsing/password derivation. Old source entries expire, and at most 1,024 entries are retained; a full table rejects additional sources rather than growing. Rejections return 429 with `Retry-After`.

In App Service hosting (identified from server-side `WEBSITE_INSTANCE_ID`), the resolver uses only `CLIENT-IP`, which the App Service frontend overwrites. It accepts a single valid IP/IP-and-port value and normalizes it. `X-Forwarded-For` and `X-Azure-ClientIP` are ignored. Before deployment, verify requests entering this Function App pass through that frontend and a caller-supplied `CLIENT-IP` is overwritten; do not enable this assumption for a self-hosted endpoint or unverified proxy. [App Service header behavior](https://github.com/Azure/app-service-linux-docs/blob/master/Things_You_Should_Know/headers.md).

The current isolated `HttpRequestData` model does not expose a socket source address. Local, missing and malformed IP data therefore share one `unknown-source` bucket; spoofed local headers cannot create extra buckets. Tests cover this behavior.

Counters reset on restart and are not shared across Azure instances. This is basic per-instance protection, not a distributed brute-force or DDoS defense. It adds no service, but uses existing Functions compute.

## Password and secret rotation

To change the password, rerun the generation script and replace `ADMIN_PASSWORD_HASH` (and the username only if desired). To invalidate all outstanding sessions, also replace `ADMIN_SESSION_SECRET` with the newly generated secret. Changing only the password does not invalidate tokens already signed by an unchanged secret. Never reuse the password itself as the session secret. Coordinate the normal restart across workers; sessions signed by the previous key stop working after those workers reload.

Logout requires no endpoint: it clears session storage and resets the editor. Re-login creates a new eight-hour session.

## Inventory and Stripe compatibility

The admin request/response contract, variant handling and conditional Blob `If-Match` writes are unchanged. Blob 412 during a manual update is mapped to `InventoryConcurrencyException`, yielding HTTP 409 without overwriting newer inventory. Reload and submit the complete variant state with the newest ETag.

Stripe continues to verify `Stripe-Signature` using `Stripe__WebhookSecret`, resolve line items with `Stripe__SecretKey`, and save decrements plus processed event IDs in the same conditional Blob write. Its existing retry/idempotency code does not depend on admin authentication. Preserve all storage and Stripe settings. Do not apply administrator middleware or login throttling to the webhook or public checkout.

## Verification

From the repository root:

```powershell
dotnet build Tacc.sln -c Release
dotnet test tests/Tacc.Api.Tests/Tacc.Api.Tests.csproj -c Release
```

From `Tacc.Site`:

```powershell
npm.cmd ci
npm.cmd run build:admin
npm.cmd run test:admin
```

The API tests use synthetic credentials, an isolated conditional Blob SDK double and signed synthetic Stripe events; they do not contact production or the developer's inventory. They cover login errors, token validation, middleware, limits, missing settings, inventory contracts, stale writes, public reads, webhook signatures and Stripe service idempotency/retries. Frontend tests use Node's built-in test runner with a small DOM/fetch harness against both source and committed bundle; they cover sessions, login errors, logout, expiry, 401, quantity validation and conflict controls. No new runtime frontend dependency is introduced.

For an actual browser check, start the local API/static host with private settings and Azurite, visit `http://localhost:7000/admin/`, log in, reload, edit a quantity, save, and log out. In two sessions verify stale writes return 409; test a signed Stripe test-mode completion independently. Ensure localhost tests use only intended local inventory.

## Manual deployment checklist (not performed by this implementation)

1. Review and run the builds/tests. Prepare the three private App Settings and preserve storage/Stripe settings. Set HTTPS-only access.
2. Inspect Function App **Authentication** (Easy Auth). The application owns authentication: platform settings must allow unauthenticated requests to reach login/public/Stripe routes and must not redirect or reject the local HS256 bearer token as an Entra token. Do not delete or change existing Entra registrations without separately reviewing their other uses.
3. Inspect Function App **CORS**. Allow exactly `https://tacclimbing.com` plus separately approved origins; no wildcard. Verify host-handled OPTIONS, GET/PUT/POST, and `Authorization`/`Content-Type` headers. Keep credentialed CORS disabled. Local CORS remains in `local.settings.json` under `Host`; `AllowedOrigins__*` entries are not consumed by application code. [Functions security/CORS](https://learn.microsoft.com/azure/azure-functions/security-concepts).
4. Publish the API in Release using the existing Azure publish profile or normal Functions deployment process. No new resources are needed. Coordinate the frontend switch: the old MSAL bundle will not authenticate against the new API.
5. In `Tacc.Site`, run `npm.cmd ci`, `npm.cmd run build:admin`, and `npm.cmd run test:admin`. Commit the regenerated `admin.bundle.js` with the changed HTML/config/CSS. Deploy the static `wwwroot` through the existing Pages workflow after review; it uploads files without rebuilding JavaScript. Pushing to `main` triggers production Pages deployment.
6. Verify login, authenticated GET/PUT, anonymous public reads, CORS preflights, generic errors/throttling, and signed Stripe test processing without any admin token. Check audit identity and ensure logs do not capture secrets. Keep the Stripe webhook URL unchanged.

No deployment, push to `main`, or live Azure configuration change is part of this implementation.

## Separate runtime follow-up

This change retains .NET 9. Its support ends **November 10, 2026**. Plan a separate supported-runtime upgrade, checking Azure Functions and the existing Linux hosting plan before choosing the target. [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).

## Implementation file manifest

Added:

- `api/Authentication/AdminAuthenticationOptions.cs`
- `api/Authentication/AdminAuthenticationService.cs`
- `api/Authentication/AdminPasswordHash.cs`
- `api/Authentication/LoginSourceResolver.cs`
- `api/Authentication/LoginThrottle.cs`
- `api/Functions/AdminLoginFunction.cs`
- `api/Scripts/New-AdminCredentials.ps1`
- `tests/Tacc.Api.Tests/Tacc.Api.Tests.csproj`
- `tests/Tacc.Api.Tests/TestSupport.cs`
- `tests/Tacc.Api.Tests/AuthenticationTests.cs`
- `tests/Tacc.Api.Tests/InventoryAndStripeTests.cs`
- `Tacc.Site/tests/admin.test.cjs`
- `docs/AdminAuthentication.md`

Modified:

- `api/Program.cs`
- `api/Authentication/AdminAuthenticationMiddleware.cs`
- `api/Services/BlobInventoryService.cs` (manual 412-to-409 error mapping only)
- `api/Tacc.Api.csproj`
- `api/local.settings.example.json`
- `api/README.md`
- `Tacc.Site/wwwroot/admin/index.html`
- `Tacc.Site/wwwroot/assets/js/admin.js`
- `Tacc.Site/wwwroot/assets/js/admin.bundle.js`
- `Tacc.Site/wwwroot/assets/js/config.js`
- `Tacc.Site/wwwroot/assets/css/admin.css`
- `Tacc.Site/package.json`
- `Tacc.Site/package-lock.json`
- `Tacc.sln`
- `README.md`

Removed:

- `api/Authentication/EntraAuthenticationOptions.cs`
- `api/Authentication/EntraTokenValidator.cs`
- `api/Authentication/InventoryAdminAuthorization.cs`

The pre-existing edit to `api/inventory.json` was not changed by this implementation. Stripe function source, shared inventory interfaces, live settings and deployment workflows were not modified.
