# TACC API

`Tacc.Api` is the .NET 9 isolated-worker Azure Functions backend for TACC. The public website remains static. Phase 2 adds a read-only inventory endpoint backed by private Azure Blob Storage; the existing anonymous health endpoint remains available.

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

Inventory is stored in the private `inventory` Blob container at `inventory.json`. The internal document is separate from the HTTP response and currently contains only the TACC shirt:

```json
{
  "products": {
    "tacc-shirt": {
      "sizes": {
        "S": 0,
        "M": 0,
        "L": 0,
        "XL": 0
      }
    }
  }
}
```

`InventoryStorageConnection` configures the Blob Storage account. Local development uses `UseDevelopmentStorage=true`; production should provide the real connection securely through Function App settings or an equivalent secret-backed configuration source.

On the first inventory request, the service creates the private container and default blob if either is missing. It never overwrites an existing blob. Reads retain the blob ETag internally to support optimistic-concurrency writes in a later phase.

Test the anonymous endpoint with:

```powershell
Invoke-RestMethod http://localhost:7071/api/inventory
```

The endpoint returns `tacc-shirt` quantities in `S`, `M`, `L`, `XL` order. Edit `inventory/inventory.json` in Azurite Storage Explorer and call the endpoint again to verify updated quantities. If storage is unavailable, the endpoint returns HTTP 503 with a safe error rather than reporting zero stock.

`GET /api/inventory` is read-only in Phase 2. There is no inventory write endpoint, checkout behavior, or website integration.

## Configuration and CORS

.NET configuration reads Function App settings and local `Values` as environment variables. Double underscores represent nested keys, so future configuration can use names such as `BlobStorage__ConnectionString`, `Stripe__SecretKey`, `AllowedOrigins__0`, and `AdminAuthentication__Authority`. Those values remain placeholders; the inventory integration uses only `InventoryStorageConnection` in this phase.

For a local static server on port 8080, start Core Tools with only that origin allowed:

```powershell
func start --cors http://localhost:8080
```

In Azure, configure CORS on the Function App for the exact production TACC origin (and any separately required staging origin). Do not use `*` in production. CORS is an environment/host setting, not hard-coded application behavior.
