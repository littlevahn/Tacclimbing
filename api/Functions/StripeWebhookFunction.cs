using System.Net;
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

public sealed class StripeWebhookFunction(
    IInventoryService inventoryService,
    IOptions<StripeOptions> stripeOptions,
    ILogger<StripeWebhookFunction> logger)
{
    private const string CheckoutSessionCompletedEvent = "checkout.session.completed";

    [Function("StripeWebhook")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stripe/webhook")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var options = stripeOptions.Value;
        if (!IsConfigured(options.WebhookSecret))
        {
            logger.LogError("Stripe webhook processing is unavailable because Stripe__WebhookSecret is not configured.");
            return request.CreateResponse(HttpStatusCode.InternalServerError);
        }

        string payload;
        using (var reader = new StreamReader(request.Body, leaveOpen: true))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                payload,
                request.Headers.TryGetValues("Stripe-Signature", out var signatures)
                    ? signatures.FirstOrDefault()
                    : null,
                options.WebhookSecret);
        }
        catch (StripeException exception)
        {
            logger.LogWarning(exception, "Stripe webhook signature validation failed.");
            return request.CreateResponse(HttpStatusCode.BadRequest);
        }

        logger.LogInformation(
            "Received Stripe event {StripeEventId} of type {StripeEventType}.",
            stripeEvent.Id,
            stripeEvent.Type);

        if (!string.Equals(stripeEvent.Type, CheckoutSessionCompletedEvent, StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Ignoring supported-but-unhandled Stripe event {StripeEventId} of type {StripeEventType}.",
                stripeEvent.Id,
                stripeEvent.Type);
            return request.CreateResponse(HttpStatusCode.OK);
        }

        if (!IsConfigured(options.SecretKey))
        {
            logger.LogError(
                "Stripe event {StripeEventId} cannot be processed because Stripe__SecretKey is not configured.",
                stripeEvent.Id);
            return request.CreateResponse(HttpStatusCode.InternalServerError);
        }

        if (stripeEvent.Data.Object is not Session session || string.IsNullOrWhiteSpace(session.Id))
        {
            logger.LogError(
                "Stripe event {StripeEventId} declared a completed checkout but did not contain a Checkout Session.",
                stripeEvent.Id);
            return request.CreateResponse(HttpStatusCode.InternalServerError);
        }

        try
        {
            var lineItems = await GetLineItemsAsync(options.SecretKey!, session.Id, cancellationToken);
            var update = await inventoryService.ProcessStripeCheckoutAsync(
                stripeEvent.Id,
                lineItems,
                cancellationToken);

            if (update.IsDuplicate)
            {
                logger.LogInformation(
                    "Ignored duplicate Stripe event {StripeEventId} for Checkout Session {CheckoutSessionId}.",
                    stripeEvent.Id,
                    session.Id);
                return request.CreateResponse(HttpStatusCode.OK);
            }

            foreach (var adjustment in update.Adjustments)
            {
                logger.LogInformation(
                    "Processed Stripe inventory line item. Event {StripeEventId}, Checkout Session {CheckoutSessionId}, product {StripeProductId}, price {StripePriceId}, purchased {PurchasedQuantity}, inventory item {InventoryKey}, before {QuantityBefore}, after {QuantityAfter}, mapped {IsMapped}, shortfall {WasShortfall}.",
                    stripeEvent.Id,
                    session.Id,
                    adjustment.StripeProductId,
                    adjustment.StripePriceId,
                    adjustment.PurchasedQuantity,
                    adjustment.InventoryKey,
                    adjustment.QuantityBefore,
                    adjustment.QuantityAfter,
                    adjustment.IsMapped,
                    adjustment.WasShortfall);
            }

            return request.CreateResponse(HttpStatusCode.OK);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Stripe event {StripeEventId} for Checkout Session {CheckoutSessionId} could not be processed.",
                stripeEvent.Id,
                session.Id);
            return request.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }

    private static async Task<IReadOnlyList<PurchasedStripeLineItem>> GetLineItemsAsync(
        string secretKey,
        string checkoutSessionId,
        CancellationToken cancellationToken)
    {
        var stripeClient = new StripeClient(secretKey);
        var lineItemService = new SessionLineItemService(stripeClient);
        var options = new SessionLineItemListOptions { Limit = 100 };
        var lineItems = new List<PurchasedStripeLineItem>();

        while (true)
        {
            var page = await lineItemService.ListAsync(
                checkoutSessionId,
                options,
                cancellationToken: cancellationToken);

            foreach (var lineItem in page.Data)
            {
                lineItems.Add(new PurchasedStripeLineItem(
                    lineItem.Price?.ProductId,
                    lineItem.Price?.Id,
                    lineItem.Quantity ?? 0));
            }

            if (!page.HasMore || page.Data.Count == 0)
            {
                return lineItems;
            }

            options.StartingAfter = page.Data[^1].Id;
        }
    }

    private static bool IsConfigured(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Contains("<not-configured>", StringComparison.OrdinalIgnoreCase);
}
