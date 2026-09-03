# TACC API

`Tacc.Api` is the .NET 9 isolated-worker Azure Functions backend for TACC. The public website remains static. Inventory reads are backed by private Azure Blob Storage, while admin-only inventory updates are protected by a Microsoft Entra workforce tenant.

## Frontend relationship

The static website lives under `Tacc.Site/wwwroot/`. `/shop/` calls anonymous `GET /api/inventory/tacc-shirt`, while `/admin/` uses MSAL Browser and the protected admin endpoints. The frontend API and public Entra identifiers are configured in `Tacc.Site/wwwroot/assets/js/config.js`; no credentials belong in that public file.

The public endpoint contract remains unchanged. Admin writes and Stripe checkout processing use conditional Blob ETags; the webhook decrements inventory only after a signed `checkout.session.completed` event. A future phase should alert the site owner/developer when the public website cannot retrieve inventory from this API.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Azure Functions Core Tools 4.x](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- Visual Studio with the Azure development workload, or a globally installed [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite), for local Blob Storage

## Restore and build

From the repository root:

```powershell
cd api
dotnet restore
dotnet build --no-restore
```

## Run locally

Debug builds and runs start Azurite automatically through `Scripts/Start-Azurite.ps1`. The script prefers a repository-local or globally installed Azurite, falls back to Visual Studio's bundled emulator, and always uses the repository's ignored `.azurite` directory so Storage Explorer and the API see the same persisted data.

If Azurite is not available through Visual Studio, install it from PowerShell with the signed command shim (which avoids the `npm.ps1` execution-policy error):

```powershell
npm.cmd install --global azurite
```

In another terminal, copy the example settings if local settings do not exist and start the Functions host:

```powershell
Copy-Item local.settings.example.json local.settings.json
dotnet run --project Tacc.Api.csproj
```

The health endpoint is available at:

```text
http://localhost:7071/api/health
```

It returns HTTP 200 with a response identifying the TACC API as healthy. `UseDevelopmentStorage=true` expects Azurite on its default local endpoints (`127.0.0.1:10000-10002`). The startup script is idempotent: it leaves a running emulator alone and never creates, migrates, or replaces the inventory blob.

`local.settings.json` is ignored by Git. Keep local settings and all credentials in that file or another environment-specific secret store; never commit secrets. Azure-hosted configuration should use Function App settings or a dedicated secret store such as Azure Key Vault.

## Inventory storage

Inventory is stored in the private `inventory` Blob container at `inventory.json`. Each inventory-managed Stripe product/size is a separate record, identified by a stable `key`. The Blob file remains the source of truth; the public and admin `tacc-shirt` endpoints group matching `tacc-shirt-*` records into their size variants.

The internal document is separate from the HTTP response and currently contains only `tacc-shirt`:

```json
{
  "products": [
    {
      "key": "tacc-shirt-small",
      "name": "TACC Shirt - Small",
      "size": "S",
      "stripeProductId": "prod_...",
      "stripePriceId": "price_...",
      "quantity": 10
    }
  ]
}
```

`stripePriceId` is the primary checkout mapping because it is unique to the sold SKU. The webhook falls back to `stripeProductId` only when a price is not represented in the Stripe line item. The service adds a `processedStripeEventIds` collection to the Blob only after the first successfully processed webhook; it is internal idempotency metadata and is not returned by either inventory API.

`InventoryStorageConnection` configures the Blob Storage account. Local development uses `UseDevelopmentStorage=true`; production should provide the real connection securely through Function App settings or an equivalent secret-backed configuration source.

The service never creates or replaces `inventory.json`. The blob must be provisioned explicitly before the API is used. If it is missing, inventory requests fail safely with `503 Service Unavailable`. Reads retain the blob ETag. The public endpoint does not expose it, but the admin endpoint returns it as an opaque concurrency token for safe updates.

The service does not create, migrate, or replace inventory data automatically. Provision the Blob explicitly from the current product-per-size schema before the API is used.

Test the anonymous endpoint with:

```powershell
Invoke-RestMethod http://localhost:7071/api/inventory/tacc-shirt
```

The anonymous `GET /api/inventory/{productId}` endpoint returns a product name and generic variant DTOs. For `tacc-shirt`, variants are returned in their stored product-record order (currently `S`, `M`, `L`, `XL`, `XXL`). Edit `inventory/inventory.json` in Azurite Storage Explorer and call the endpoint again to verify updated quantities. An unknown product returns HTTP 404. If storage is unavailable, the endpoint returns HTTP 503 with a safe error rather than reporting zero stock.

`GET /api/inventory/{productId}` is anonymous and read-only. It remains the public shop API and does not require a bearer token. Azurite supplies Blob Storage locally; Azure Blob Storage supplies it in production.

## Configuration and CORS

.NET configuration reads Function App settings and local `Values` as environment variables. Double underscores represent nested keys. The admin API requires these non-secret Entra application settings:

```text
Entra__TenantId=<workforce tenant ID>
Entra__ClientId=<API application client ID>
Entra__Authority=https://login.microsoftonline.com/<tenant ID>/v2.0
Entra__Audience=api://<API application client ID>
Entra__AdminRole=Tacc.Inventory.Admin
Entra__AdminScope=Inventory.Manage
```

`Entra__Audience` must match the `aud` claim issued for the API access token. The API requires both the delegated `Inventory.Manage` value in the token's `scp` claim and the `Tacc.Inventory.Admin` value in its `roles` claim. Configure the scope and role on the TACC API registration, grant the TACC Admin SPA delegated permission, and assign the app role to permitted users/groups or B2B guests. Tenant/application IDs are configuration, but secrets must stay in local settings, Function App settings, or Key Vault and must never be committed.

For the Visual Studio local host, `local.settings.json` should contain the same `Host` section provided by `local.settings.example.json`:

```json
"Host": {
  "LocalHttpPort": 7071,
  "CORS": "http://localhost:7000,https://localhost:7001",
  "CORSCredentials": false
}
```

This keeps the API on `http://localhost:7071` and allows only the two configured `Tacc.Site` development origins. The committed example contains no secrets; the real `local.settings.json` remains ignored. Production CORS is configured separately on the Azure Function App and must not use a wildcard.

In Azure, configure CORS on the Function App for the exact production TACC origin (and any separately required staging origin). Do not use `*` in production. CORS is an environment/host setting, not hard-coded application behavior. The approved browser origins must allow the `Authorization` and `Content-Type` request headers so the admin frontend can send bearer-authenticated GET and PUT requests.

## Admin inventory API

The static admin frontend uses:

```text
GET /api/admin/inventory/{productId}
PUT /api/admin/inventory/{productId}
```

Both require a valid bearer access token issued by the configured Microsoft Entra workforce authority, the `Inventory.Manage` delegated scope (or the scope set in `Entra__AdminScope`), and the `Tacc.Inventory.Admin` app role (or the role set in `Entra__AdminRole`). Authentication is evaluated only for the admin functions; health and the public inventory endpoint remain anonymous. An absent or invalid bearer token receives `401 Unauthorized`; a valid token without either required authorization claim receives `403 Forbidden`.

Admin GET returns the product state and an opaque ETag:

```json
{
  "productId": "tacc-shirt",
  "name": "TACC Shirt",
  "etag": "\"0x8...\"",
  "variants": [
    { "variantId": "S", "quantity": 2 },
    { "variantId": "M", "quantity": 7 },
    { "variantId": "L", "quantity": 12 },
    { "variantId": "XL", "quantity": 21 }
  ]
}
```

Use that ETag unchanged with a complete variant state when sending PUT:

```json
{
  "etag": "\"0x8...\"",
  "variants": { "S": 12, "M": 8, "L": 5, "XL": 0 }
}
```

Every quantity must be a non-negative integer. Submitted variants must exactly match the existing variant set. PUT conditionally writes the complete `inventory.json` document using the ETag while changing only the requested product, so other products are preserved. If another writer changes the blob first, the API returns `409 Conflict`; reload with GET and retry. Unknown products return `404`, invalid data returns `400`, and storage failures return a safe `503`. A successful PUT returns the new state and ETag.

Successful changes are structured-log events containing the admin `oid` (or `sub`), product ID, before/after quantities, and Function invocation ID. Tokens, authorization headers, and secrets are not logged.

## Stripe checkout webhook

The shop starts a hosted Stripe Checkout Session with:

```text
POST /api/stripe/checkout
```

The request contains the internal product and selected variant IDs. The API resolves the current `stripePriceId` from `inventory.json`, rejects missing or out-of-stock variants, and returns only Stripe's hosted HTTPS Checkout URL. The browser never chooses a trusted Stripe Price ID.

Configure Stripe to send `checkout.session.completed` events to:

```text
https://<YOUR_FUNCTION_APP>.azurewebsites.net/api/stripe/webhook
```

For local forwarding, the route is `http://localhost:7071/api/stripe/webhook`. The endpoint uses Stripe.net's `EventUtility.ConstructEvent` with the `Stripe-Signature` header and `Stripe__WebhookSecret`; invalid signatures receive HTTP 400. It acknowledges valid but unsupported events and duplicate event IDs with HTTP 200 so Stripe does not retry intentional no-ops. A processing failure receives HTTP 500, allowing Stripe to retry.

For a completed checkout, the API retrieves every Checkout Session line item using `Stripe__SecretKey`, maps its `price_...` ID (or, when necessary, `prod_...`) to a record in `inventory.json`, and subtracts the full line-item quantity. Unknown products are logged and skipped; a quantity larger than current inventory is logged as a shortfall and clamps that record to zero rather than allowing a negative value.

The inventory adjustment and the event ID are saved in one `If-Match` Blob ETag write. A conditional-write conflict reloads and retries the checkout up to five times; a retry then sees the already-recorded ID if another instance completed it. This protects concurrent checkouts and Stripe retries without a separate database.

Add these private Function App settings (or local-only values) and never commit real values:

```text
Stripe__SecretKey=sk_...
Stripe__WebhookSecret=whsec_...
Stripe__CheckoutSuccessUrl=https://tacclimbing.com/shop/?checkout=success
Stripe__CheckoutCancelUrl=https://tacclimbing.com/shop/?checkout=cancelled
Stripe__AllowedShippingCountries__0=US
Stripe__ShippingRateId=shr_... # optional; omit only when no separate shipping charge applies
```

## Testing authenticated admin endpoints locally

There is no local authentication bypass. Configure Entra values in ignored `local.settings.json`, obtain an API access token containing both the delegated `Inventory.Manage` scope and the assigned `Tacc.Inventory.Admin` role, and call the Functions host:

```powershell
$token = '<Entra API access token>'
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod http://localhost:7071/api/admin/inventory/tacc-shirt -Headers $headers
```

Use the returned `etag` in PUT. Test without a header for `401`, and with valid tokens lacking either the delegated scope or app role for `403`. Never paste access tokens into source files, logs, or issue trackers.
