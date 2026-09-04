# TACC Website and API

TACC combines a static HTML/CSS/JavaScript website with a .NET 9 isolated-worker Azure Functions API backed by private Azure Blob Storage.

## Solution layout

```text
Tacc.sln
|-- Tacc.Site/
|   |-- Tacc.Site.csproj       Minimal local static-file host
|   |-- package.json           MSAL Browser bundle build
|   |-- Program.cs
|   `-- wwwroot/               Production-ready static website
|       |-- index.html
|       |-- how-to-use/index.html
|       |-- shop/index.html
|       |-- admin/index.html
|       |-- assets/
|       `-- CNAME
`-- api/
    `-- Tacc.Api.csproj        Azure Functions v4 isolated worker
```

`Tacc.Site` contains no MVC, Razor Pages, Blazor, controllers, APIs, authentication, database access, or business logic. It exists only to give the static site a proper local HTTP origin during development.

## Visual Studio local development

1. Open `Tacc.sln` in Visual Studio 2022.
2. Ensure `api/local.settings.json` exists. If needed, copy `api/local.settings.example.json` and provide the local-only settings.
3. In Visual Studio 2022 17.11 or later, select the shared `TACC Site + API` launch profile.
4. Press F5. The API's local service dependency starts Azurite automatically.
5. Open `http://localhost:7000/shop/index.html` and verify the public inventory state loads.
6. Open `http://localhost:7000/admin/` to test the Entra-authenticated admin page.

The shared `Tacc.slnLaunch` profile starts both projects. If the Visual Studio version does not expose shared multi-project launch profiles, configure it manually:

```text
Solution
-> Properties
-> Configure Startup Projects
-> Multiple startup projects

Tacc.Site   Start
Tacc.Api    Start
```

Use the `http` launch profile for `Tacc.Site` when testing the shop against the local HTTP Function host.

### Expected services

```text
Tacc.Site   http://localhost:7000
            https://localhost:7001

Tacc.Api    http://localhost:7071

Azurite     Local Azure Storage emulator
```

The local Functions configuration allows only these frontend origins:

```text
http://localhost:7000
https://localhost:7001
```

Those values live in the ignored `api/local.settings.json` and the committed `api/local.settings.example.json` under the `Host.CORS` setting. Production CORS remains an Azure Function App setting and should allow only approved deployed origins.

## Command-line development

Visual Studio starts Azurite automatically. For command-line development, start Azurite manually, then run each project in a separate terminal:

```powershell
dotnet run --project Tacc.Site/Tacc.Site.csproj --launch-profile http
```

```powershell
dotnet run --project api/Tacc.Api.csproj
```

Open `http://localhost:7000/shop/index.html` or `http://localhost:7000/admin/`. Do not open either page through a `file:///` URL; browser API and authentication redirects require an HTTP origin.

## Static production deployment

ASP.NET Core is not required in production. Deploy the raw contents of `Tacc.Site/wwwroot/` to the static host. The directory includes the HTML routes, CSS, JavaScript, images, icons, and `CNAME` needed by the public site.

The local host only runs:

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
```

No production page depends on server rendering or ASP.NET routing.

## Public site

- `/index.html` — Surface Lock product overview and existing purchase path
- `/how-to-use/index.html` — application instructions, session reset, FAQ, and formula
- `/shop/index.html` — TACC Shirt inventory and size selection
- `/admin/index.html` — non-prominent, Entra-authenticated inventory administration

Shared styling lives in `Tacc.Site/wwwroot/assets/css/styles.css`, with admin-only styling in `assets/css/admin.css`. Shared navigation behavior lives in `assets/js/site.js`, public environment configuration in `assets/js/config.js`, shop inventory behavior in `assets/js/shop.js`, and the admin source in `assets/js/admin.js`.

## Shop inventory configuration

The shop requests inventory once per page load:

```http
GET http://localhost:7071/api/inventory/tacc-shirt
```

The shirt size controls are generated from the API's `variants` array in stored order. Adding another valid
`tacc-shirt-*` inventory record therefore adds its size to the public shop without an HTML or JavaScript size-list change.

`Tacc.Site/wwwroot/assets/js/config.js` centralizes the public API base URL. Local `localhost` and `127.0.0.1` pages automatically use the Functions Core Tools port, while production uses the configured Azure Function App. Never put credentials or secrets in frontend configuration.

Inventory rules remain:

- Above 10: no stock wording
- 6–10: `Limited stock`
- 1–5: exact quantity remaining
- 0 or unknown API status: `More coming soon`

The error state remains internally distinct from real zero inventory. For an available selected size, the shop posts
the product and variant IDs to `POST /api/stripe/checkout` and redirects only to the validated hosted Stripe Checkout URL
returned by the API. Stripe Price IDs and secret configuration remain server-side.

## Inventory admin page

`/admin/` is a static Microsoft Entra single-page application. It uses the existing two-registration architecture:

- `TACC Admin` is the public SPA client. It has no client secret.
- `TACC` is the protected Functions API. It exposes the delegated `Inventory.Manage` permission and defines the `Tacc.Inventory.Admin` app role.

The page signs in through the workforce tenant, acquires an API access token through MSAL Browser, and sends that token only to the configured TACC Function App. The API remains authoritative and requires both the `Inventory.Manage` delegated scope and the `Tacc.Inventory.Admin` role. The admin page manages `tacc-shirt` through the existing concurrency-safe GET/PUT contract and retains the returned ETag after every successful load or save.

Configure these public values once in `Tacc.Site/wwwroot/assets/js/config.js`:

```text
productionApiBaseUrl    Public HTTPS origin of the Azure Function App
adminClientId           TACC Admin Application (client) ID
tenantId                Workforce Directory (tenant) ID
inventoryManageScope    api://<TACC_API_CLIENT_ID>/Inventory.Manage
```

Client IDs, tenant IDs, authority URLs, API scopes, and public API origins are identifiers—not secrets. Never put a client secret, storage connection string, account key, bearer token, Stripe secret, or webhook secret in this file.

MSAL Browser is installed from the supported `@azure/msal-browser` npm package and bundled into the committed static `admin.bundle.js`; Node is a build-time tool only and is not required by GitHub Pages. After changing `admin.js` or the MSAL version, rebuild it:

```powershell
cd Tacc.Site
npm install
npm run build:admin
```

### Entra configuration

In the `TACC Admin` app registration:

1. Keep the platform type set to **Single-page application**.
2. Keep the production redirect URI `https://tacclimbing.com/admin/`.
3. Add `http://localhost:7000/admin/` as the local-development SPA redirect URI.
4. Under API permissions, add delegated access to `TACC` → `Inventory.Manage` and grant the consent required by the tenant policy.

To authorize a user or B2B guest, open **Enterprise applications** → **TACC** → **Users and groups**, add the user or group, and assign the `Tacc.Inventory.Admin` role. Guest users can use the tenant's configured email one-time-passcode flow; the page does not assume an account domain or account type.

The Azure Function App CORS allowlist must include exactly the frontend origins that need access, including `https://tacclimbing.com` in production and `http://localhost:7000` for local testing. The browser preflight must allow the `Authorization` and `Content-Type` request headers and GET/PUT methods. Do not use wildcard production CORS.

### Admin test flow

1. Start Azurite, the API, and the local static host.
2. Visit `http://localhost:7000/admin/` and sign in as a role-assigned user or guest.
3. Confirm current quantities load from `GET /api/admin/inventory/tacc-shirt`.
4. Change a whole-number quantity and save; confirm the success message and new Blob value.
5. Open a second session, save newer data there, and verify the first session receives the ETag conflict prompt instead of overwriting it.
6. Test a signed-in user without the app role and confirm the permission message is shown.

## Instagram configuration

Set the approved public profile URL in `Tacc.Site/wwwroot/assets/js/site.js` by replacing `INSTAGRAM_URL_HERE`. Until then, the shared script preserves the neutral Instagram fallback and accessible configuration label.

## Backend

The API routes remain unchanged:

```http
GET /api/health
GET /api/inventory/{productId}
GET /api/admin/inventory/{productId}
PUT /api/admin/inventory/{productId}
```

See `api/README.md` for Blob Storage, Azurite, inventory data, and backend configuration details.

## Project constraints

- Keep the public deployment static under `Tacc.Site/wwwroot/`.
- Keep `Tacc.Site` free of application logic and backend frameworks.
- Keep `local.settings.json`, credentials, and production secrets out of source control.
- Keep admin authentication and authorization enforced by Microsoft Entra and the API; hiding `/admin/` is not a security control.
- Keep Stripe Price IDs, secret keys, webhook secrets, and trusted checkout configuration server-side.

## Monitoring TODO

A future phase should alert the site owner/developer when the public website cannot retrieve inventory from the Azure Functions API. Phase 4 logs the browser failure and shows a safe fallback only.
