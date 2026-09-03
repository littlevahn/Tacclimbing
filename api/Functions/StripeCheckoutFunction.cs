using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using Tacc.Api.Models.Inventory;
using Tacc.Api.Services;
using Tacc.Api.Stripe;

namespace Tacc.Api.Functions;

public sealed class StripeCheckoutFunction(
    IInventoryService inventoryService,
    IOptions<StripeOptions> stripeOptions,
    ILogger<StripeCheckoutFunction> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Function("StripeCheckout")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stripe/checkout")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        CheckoutRequest? checkoutRequest;
        try
        {
            checkoutRequest = await JsonSerializer.DeserializeAsync<CheckoutRequest>(
                request.Body,
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return await WriteErrorAsync(request, HttpStatusCode.BadRequest, "A valid product and variant are required.", cancellationToken);
        }

        var productId = checkoutRequest?.ProductId?.Trim();
        var variantId = checkoutRequest?.VariantId?.Trim();
        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(variantId))
        {
            return await WriteErrorAsync(request, HttpStatusCode.BadRequest, "A valid product and variant are required.", cancellationToken);
        }

        var options = stripeOptions.Value;
        if (!IsConfigured(options.SecretKey) ||
            !IsAbsoluteHttpsUrl(options.CheckoutSuccessUrl) ||
            !IsAbsoluteHttpsUrl(options.CheckoutCancelUrl))
        {
            logger.LogError("Stripe checkout is unavailable because its secret key or HTTPS return URLs are not configured.");
            return await WriteErrorAsync(request, HttpStatusCode.ServiceUnavailable, "Checkout is temporarily unavailable.", cancellationToken);
        }

        try
        {
            var item = await inventoryService.GetCheckoutItemAsync(productId, variantId, cancellationToken);
            if (item is null)
            {
                logger.LogInformation("Checkout was requested for unknown inventory item {ProductId}/{VariantId}.", productId, variantId);
                return await WriteErrorAsync(request, HttpStatusCode.NotFound, "The selected product is unavailable.", cancellationToken);
            }

            if (item.Quantity <= 0)
            {
                logger.LogInformation("Checkout was requested for out-of-stock inventory item {InventoryKey}.", item.InventoryKey);
                return await WriteErrorAsync(request, HttpStatusCode.Conflict, "The selected size is out of stock.", cancellationToken);
            }

            var sessionOptions = new SessionCreateOptions
            {
                Mode = "payment",
                SuccessUrl = options.CheckoutSuccessUrl,
                CancelUrl = options.CheckoutCancelUrl,
                ClientReferenceId = item.InventoryKey,
                Metadata = new Dictionary<string, string>
                {
                    ["inventory_key"] = item.InventoryKey,
                    ["product_id"] = item.ProductId,
                    ["variant_id"] = item.VariantId
                },
                LineItems =
                [
                    new SessionLineItemOptions
                    {
                        Price = item.StripePriceId,
                        Quantity = 1
                    }
                ],
                ShippingAddressCollection = new SessionShippingAddressCollectionOptions
                {
                    AllowedCountries = options.AllowedShippingCountries
                        .Where(country => !string.IsNullOrWhiteSpace(country))
                        .Select(country => country.Trim().ToUpperInvariant())
                        .Distinct(StringComparer.Ordinal)
                        .ToList()
                }
            };

            if (IsConfigured(options.ShippingRateId))
            {
                sessionOptions.ShippingOptions =
                [
                    new SessionShippingOptionOptions { ShippingRate = options.ShippingRateId }
                ];
            }

            var stripeClient = new StripeClient(options.SecretKey);
            var session = await stripeClient.V1.Checkout.Sessions.CreateAsync(
                sessionOptions,
                cancellationToken: cancellationToken);

            if (!Uri.TryCreate(session.Url, UriKind.Absolute, out var checkoutUri) ||
                checkoutUri.Scheme != Uri.UriSchemeHttps ||
                !checkoutUri.Host.EndsWith(".stripe.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Stripe did not return a valid hosted Checkout URL.");
            }

            logger.LogInformation(
                "Created Stripe Checkout Session {CheckoutSessionId} for {InventoryKey}; remaining recorded quantity {Quantity}.",
                session.Id,
                item.InventoryKey,
                item.Quantity);

            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new CheckoutResponse(session.Url), cancellationToken);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StripeException exception)
        {
            logger.LogError(exception, "Stripe could not create Checkout for {ProductId}/{VariantId}.", productId, variantId);
            return await WriteErrorAsync(request, HttpStatusCode.BadGateway, "Checkout could not be started.", cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Checkout creation failed for {ProductId}/{VariantId}.", productId, variantId);
            return await WriteErrorAsync(request, HttpStatusCode.ServiceUnavailable, "Checkout is temporarily unavailable.", cancellationToken);
        }
    }

    private static bool IsConfigured(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Contains("<not-configured>", StringComparison.OrdinalIgnoreCase);

    private static bool IsAbsoluteHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static async Task<HttpResponseData> WriteErrorAsync(
        HttpRequestData request,
        HttpStatusCode statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new InventoryErrorResponse(message), cancellationToken);
        return response;
    }

    private sealed record CheckoutRequest(
        [property: JsonPropertyName("productId")] string? ProductId,
        [property: JsonPropertyName("variantId")] string? VariantId);

    private sealed record CheckoutResponse(
        [property: JsonPropertyName("url")] string Url);
}
