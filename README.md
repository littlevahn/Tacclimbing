# TACC Website and API

TACC combines a static HTML/CSS/JavaScript website with a .NET 9 isolated-worker Azure Functions API backed by private Azure Blob Storage.

## Solution layout

```text
Tacc.sln
|-- Tacc.Site/
|   |-- Tacc.Site.csproj       Minimal local static-file host
|   |-- Program.cs
|   `-- wwwroot/               Production-ready static website
|       |-- index.html
|       |-- how-to-use/index.html
|       |-- shop/index.html
|       |-- assets/
|       `-- CNAME
`-- api/
    `-- Tacc.Api.csproj        Azure Functions v4 isolated worker
```

`Tacc.Site` contains no MVC, Razor Pages, Blazor, controllers, APIs, authentication, database access, or business logic. It exists only to give the static site a proper local HTTP origin during development.

## Visual Studio local development

1. Open `Tacc.sln` in Visual Studio 2022.
2. Ensure `api/local.settings.json` exists. If needed, copy `api/local.settings.example.json` and provide the local-only settings.
3. Start Azurite so the Functions project can read the local inventory blob.
4. In Visual Studio 2022 17.11 or later, select the shared `TACC Site + API` launch profile.
5. Press F5.
6. Open `http://localhost:7000/shop/index.html` and verify the inventory state loads.

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

Start Azurite, then run each project in a separate terminal:

```powershell
dotnet run --project Tacc.Site/Tacc.Site.csproj --launch-profile http
```

```powershell
cd api
func start
```

Open `http://localhost:7000/shop/index.html`. Do not open the page through a `file:///` URL; browser API requests require an HTTP origin.

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

All custom styling lives in `Tacc.Site/wwwroot/assets/css/styles.css`. Shared navigation behavior lives in `assets/js/site.js`, public environment configuration in `assets/js/config.js`, and shop inventory behavior in `assets/js/shop.js`.

## Shop inventory configuration

The shop requests inventory once per page load:

```http
GET http://localhost:7071/api/inventory/tacc-shirt
```

`Tacc.Site/wwwroot/assets/js/config.js` centralizes the public API origin. Local `localhost` and `127.0.0.1` pages automatically use the Functions Core Tools port. For production on a separate Function App, replace the production empty value with the approved public HTTPS origin. Never put credentials or secrets in frontend configuration.

Inventory rules remain:

- Above 10: no stock wording
- 6–10: `Limited stock`
- 1–5: exact quantity remaining
- 0 or unknown API status: `More coming soon`

The error state remains internally distinct from real zero inventory. Phase 4 performs no checkout request, inventory write, polling, or payment processing.

## Instagram configuration

Set the approved public profile URL in `Tacc.Site/wwwroot/assets/js/site.js` by replacing `INSTAGRAM_URL_HERE`. Until then, the shared script preserves the neutral Instagram fallback and accessible configuration label.

## Backend

The API routes remain unchanged:

```http
GET /api/health
GET /api/inventory/{productId}
```

See `api/README.md` for Blob Storage, Azurite, inventory data, and backend configuration details.

## Project constraints

- Keep the public deployment static under `Tacc.Site/wwwroot/`.
- Keep `Tacc.Site` free of application logic and backend frameworks.
- Keep `local.settings.json`, credentials, and production secrets out of source control.
- Do not add Stripe, checkout requests, inventory decrement/reservation, admin features, or monitoring alerts in this phase.

## Monitoring TODO

A future phase should alert the site owner/developer when the public website cannot retrieve inventory from the Azure Functions API. Phase 4 logs the browser failure and shows a safe fallback only.
