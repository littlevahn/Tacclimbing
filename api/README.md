# TACC API

`Tacc.Api` is the isolated .NET Azure Functions backend for future TACC server-side features. The public website remains static, and this project currently exposes only an anonymous health endpoint.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure Functions Core Tools 4.x](https://learn.microsoft.com/azure/azure-functions/functions-run-local)

## Restore and build

From the repository root:

```powershell
cd api
dotnet restore
dotnet build --no-restore
```

## Run locally

Copy `local.settings.example.json` to `local.settings.json`, replace the storage placeholder with a valid local development setting, and then start the Functions host:

```powershell
Copy-Item local.settings.example.json local.settings.json
dotnet run
```

The health endpoint is available at:

```text
http://localhost:7071/api/health
```

It returns HTTP 200 with a response identifying the TACC API as healthy.

`local.settings.json` is ignored by Git. Keep local settings and all credentials in that file or another environment-specific secret store; never commit secrets. Azure-hosted configuration should use Function App settings or a dedicated secret store such as Azure Key Vault.

## Configuration and CORS

.NET configuration reads Function App settings and local `Values` as environment variables. Double underscores represent nested keys, so future configuration can use names such as `BlobStorage__ConnectionString`, `Stripe__SecretKey`, `AllowedOrigins__0`, and `AdminAuthentication__Authority`. The example values are placeholders only; no storage, Stripe, or authentication integration exists in this phase.

For a local static server on port 8080, start Core Tools with only that origin allowed:

```powershell
dotnet run -- --cors http://localhost:8080
```

In Azure, configure CORS on the Function App for the exact production TACC origin (and any separately required staging origin). Do not use `*` in production. CORS is an environment/host setting, not hard-coded application behavior.
