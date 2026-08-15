# TACC API

`Tacc.Api` is the .NET 9 isolated-worker Azure Functions backend for TACC. The public website remains static. Phase 2 adds a read-only inventory endpoint backed by private Azure Blob Storage; the existing anonymous health endpoint remains available.

## Frontend relationship

The static website lives under `Tacc.Site/wwwroot/` and uses `/shop/` as its dedicated merchandise page. In Phase 4, that page calls anonymous `GET /api/inventory/tacc-shirt` once on load and maps the returned generic variants to the shirt's S, M, L, and XL controls. The frontend API origin is configured in `Tacc.Site/wwwroot/assets/js/config.js`; no credentials belong in that public file.

Both backend endpoints, the Blob schema, and read-only behavior remain unchanged. Phase 4 adds no inventory writes, checkout processing, Stripe integration, polling, admin functionality, or monitoring. A future phase should alert the site owner/developer when the public website cannot retrieve inventory from this API.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Azure Functions Core Tools 4.x](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) for local Blob Storage

## Restore and build

From the repository root:

```powershell
cd api
dotnet restore
dotnet build --no-restore
```

## Run locally

Start Azurite:

```powershell
azurite
```

In another terminal, copy the example settings if local settings do not exist and start the Functions host:

```powershell
Copy-Item local.settings.example.json local.settings.json
func start
```

The health endpoint is available at:

```text
http://localhost:7071/api/health
```

It returns HTTP 200 with a response identifying the TACC API as healthy. `UseDevelopmentStorage=true` expects Azurite on its default local endpoints.

`local.settings.json` is ignored by Git. Keep local settings and all credentials in that file or another environment-specific secret store; never commit secrets. Azure-hosted configuration should use Function App settings or a dedicated secret store such as Azure Key Vault.

## Inventory storage

Inventory is stored in the private `inventory` Blob container at `inventory.json`. Product IDs are stable dictionary keys, and every product contains generic variants. The current variants happen to represent shirt sizes, but future products can use identifiers such as colors or `default` without changing the model or public API.

The internal document is separate from the HTTP response and currently contains only `tacc-shirt`:

```json
{
  "products": {
    "tacc-shirt": {
      "name": "TACC Shirt",
      "variants": {
        "S": {
          "quantity": 0
        },
        "M": {
          "quantity": 0
        },
        "L": {
          "quantity": 0
        },
        "XL": {
          "quantity": 0
        }
      }
    }
  }
}
```

`InventoryStorageConnection` configures the Blob Storage account. Local development uses `UseDevelopmentStorage=true`; production should provide the real connection securely through Function App settings or an equivalent secret-backed configuration source.

On the first inventory request, the service creates the private container and default blob if either is missing. It never overwrites an existing blob. Reads retain the blob ETag internally to support optimistic-concurrency writes in a later phase.

An inventory blob created before the multi-product revision must be manually converted to the `name` and `variants` structure above. If the local data is disposable, delete only `inventory/inventory.json` and let the next request recreate it. The service does not migrate or overwrite existing quantities automatically.

Test the anonymous endpoint with:

```powershell
Invoke-RestMethod http://localhost:7071/api/inventory/tacc-shirt
```

The anonymous `GET /api/inventory/{productId}` endpoint returns a product name and generic variant DTOs. For `tacc-shirt`, variants are returned in stored `S`, `M`, `L`, `XL` order. Edit `inventory/inventory.json` in Azurite Storage Explorer and call the endpoint again to verify updated quantities. An unknown product returns HTTP 404. If storage is unavailable, the endpoint returns HTTP 503 with a safe error rather than reporting zero stock.

`GET /api/inventory/{productId}` is read-only in Phase 2. There is no inventory write endpoint, checkout behavior, or website integration. Azurite supplies Blob Storage locally; Azure Blob Storage supplies it in production.

## Configuration and CORS

.NET configuration reads Function App settings and local `Values` as environment variables. Double underscores represent nested keys, so future configuration can use names such as `BlobStorage__ConnectionString`, `Stripe__SecretKey`, `AllowedOrigins__0`, and `AdminAuthentication__Authority`. Those values remain placeholders; the inventory integration uses only `InventoryStorageConnection` in this phase.

For the Visual Studio local host, `local.settings.json` should contain the same `Host` section provided by `local.settings.example.json`:

```json
"Host": {
  "LocalHttpPort": 7071,
  "CORS": "http://localhost:7000,https://localhost:7001",
  "CORSCredentials": false
}
```

This keeps the API on `http://localhost:7071` and allows only the two configured `Tacc.Site` development origins. The committed example contains no secrets; the real `local.settings.json` remains ignored. Production CORS is configured separately on the Azure Function App and must not use a wildcard.

In Azure, configure CORS on the Function App for the exact production TACC origin (and any separately required staging origin). Do not use `*` in production. CORS is an environment/host setting, not hard-coded application behavior.
