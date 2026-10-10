# TACC Website and API

TACC combines a static HTML/CSS/JavaScript website with a .NET 9 isolated-worker Azure Functions API backed by private Azure Blob Storage.

## Solution layout

```text
Tacc.sln
|-- Tacc.Site/
|   |-- Tacc.Site.csproj       Minimal local static-file host
|   |-- package.json           Admin JavaScript bundle build
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
6. Open `http://localhost:7000/admin/` to test the password-authenticated admin page.

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

Open `http://localhost:7000/shop/index.html` or `http://localhost:7000/admin/`. Do not open either page through a `file:///` URL; browser API calls and login require an HTTP origin.

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
- `/admin/index.html` — non-prominent, password-authenticated inventory administration

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

`/admin/` uses a single administrator username/password configured privately in Azure Functions. It calls `POST /api/admin/login`, stores the signed eight-hour token in browser `sessionStorage`, and sends it through `Authorization: Bearer` on the existing admin inventory GET/PUT requests. Logout, expiration and API 401 clear the session. The inventory editor, dynamic variants and ETag handling are preserved.

The public `config.js` contains only the API base URL and product ID; no credentials or Function keys belong in frontend files. Entra and MSAL are no longer used. Both admin endpoints enforce authentication on the server. Public inventory, health and Stripe endpoints remain independent.

Generate credentials offline from the repository root:

```powershell
powershell.exe -NoProfile -File .\api\Scripts\New-AdminCredentials.ps1 -Username owner
```

Copy the generated `ADMIN_USERNAME`, `ADMIN_PASSWORD_HASH` and `ADMIN_SESSION_SECRET` into private Function App settings or ignored `api/local.settings.json` under `Values`. Never commit the output. Missing/invalid auth settings disable only administrator access.

After changing admin JavaScript, rebuild the committed bundle from `Tacc.Site`:

```powershell
npm.cmd ci
npm.cmd run build:admin
npm.cmd run test:admin
```

Run `dotnet test tests/Tacc.Api.Tests/Tacc.Api.Tests.csproj -c Release` from the repository root. The automated tests use synthetic credentials and isolated storage doubles. For a manual check, start the local API/static host and Azurite, sign in, reload, edit/save, verify the two-session conflict control and log out.

See [administrator authentication operations](docs/AdminAuthentication.md) for configuration, password/secret rotation, CORS, Azure Easy Auth checks, throttling limits, test coverage and manual deployment steps. The existing Pages workflow uploads `wwwroot` without rebuilding JavaScript; coordinate API and frontend deployment after review. No deployment is performed by the implementation task.

## Instagram configuration

Set the approved public profile URL in `Tacc.Site/wwwroot/assets/js/site.js` by replacing `INSTAGRAM_URL_HERE`. Until then, the shared script preserves the neutral Instagram fallback and accessible configuration label.

## Backend

The API exposes:

```http
POST /api/admin/login
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
- Keep administrator authentication enforced by the API; hiding `/admin/` is not a security control.
- Keep Stripe Price IDs, secret keys, webhook secrets, and trusted checkout configuration server-side.

## Monitoring TODO

A future phase should alert the site owner/developer when the public website cannot retrieve inventory from the Azure Functions API. Phase 4 logs the browser failure and shows a safe fallback only.

## Runtime follow-up

This work retains .NET 9. Its support ends **November 10, 2026**; plan a separate supported-runtime upgrade. See [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).
