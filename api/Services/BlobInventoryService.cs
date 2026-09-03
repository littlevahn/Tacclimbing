using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Tacc.Api.Models.Inventory;

namespace Tacc.Api.Services;

public sealed class BlobInventoryService(
    BlobServiceClient blobServiceClient,
    ILogger<BlobInventoryService> logger) : IInventoryService
{
    private const string ContainerName = "inventory";
    private const string BlobName = "inventory.json";
    private const int MaxCheckoutWriteAttempts = 5;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<ProductInventoryResult?> GetProductInventoryAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        try
        {
            var (document, eTag, _) = await DownloadAsync(cancellationToken);
            var products = FindProducts(document, productId);
            if (products.Count == 0)
            {
                return null;
            }

            return CreateProductResult(productId, products, eTag);
        }
        catch (RequestFailedException exception)
        {
            logger.LogError(exception, "Blob Storage failed while retrieving inventory (status {Status}).", exception.Status);
            throw;
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The inventory blob contains invalid JSON.");
            throw;
        }
        catch (InvalidDataException exception)
        {
            logger.LogError(exception, "The inventory blob contains invalid product data.");
            throw;
        }
    }

    public async Task<ProductInventoryUpdateResult?> UpdateProductInventoryAsync(
        string productId,
        string expectedETag,
        IReadOnlyDictionary<string, int> quantities,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedETag);
        ArgumentNullException.ThrowIfNull(quantities);

        try
        {
            var (document, _, blobClient) = await DownloadAsync(cancellationToken);
            var products = FindProducts(document, productId);
            if (products.Count == 0)
            {
                return null;
            }

            ValidateVariantUpdate(productId, products, quantities);
            var previousQuantities = products.ToDictionary(
                product => product.Size,
                product => product.Quantity,
                StringComparer.Ordinal);

            foreach (var product in products)
            {
                product.Quantity = quantities[product.Size];
            }

            var newETag = await UploadAsync(document, blobClient, new ETag(expectedETag), cancellationToken);
            return new ProductInventoryUpdateResult(
                CreateProductResult(productId, products, newETag),
                previousQuantities);
        }
        catch (InventoryValidationException)
        {
            throw;
        }
        catch (InventoryConcurrencyException)
        {
            throw;
        }
        catch (RequestFailedException exception)
        {
            logger.LogError(exception, "Blob Storage failed while updating inventory (status {Status}).", exception.Status);
            throw;
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The inventory blob contains invalid JSON.");
            throw;
        }
        catch (InvalidDataException exception)
        {
            logger.LogError(exception, "The inventory blob contains invalid product data.");
            throw;
        }
    }

    public async Task<StripeInventoryUpdateResult> ProcessStripeCheckoutAsync(
        string stripeEventId,
        IReadOnlyList<PurchasedStripeLineItem> lineItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeEventId);
        ArgumentNullException.ThrowIfNull(lineItems);

        for (var attempt = 1; attempt <= MaxCheckoutWriteAttempts; attempt++)
        {
            try
            {
                var (document, eTag, blobClient) = await DownloadAsync(cancellationToken);
                if (document.ProcessedStripeEventIds.Contains(stripeEventId))
                {
                    logger.LogInformation("Stripe event {StripeEventId} was already processed.", stripeEventId);
                    return new StripeInventoryUpdateResult(true, []);
                }

                var adjustments = ApplyCheckout(document, stripeEventId, lineItems);
                document.ProcessedStripeEventIds.Add(stripeEventId);

                try
                {
                    await UploadAsync(document, blobClient, eTag, cancellationToken);
                    return new StripeInventoryUpdateResult(false, adjustments);
                }
                catch (RequestFailedException exception) when (exception.Status == 412 && attempt < MaxCheckoutWriteAttempts)
                {
                    logger.LogInformation(
                        "Inventory changed while processing Stripe event {StripeEventId}; retrying attempt {Attempt}.",
                        stripeEventId,
                        attempt + 1);
                }
                catch (RequestFailedException exception) when (exception.Status == 412)
                {
                    throw new InventoryConcurrencyException(
                        "Inventory changed too frequently to process the Stripe checkout.", exception);
                }
            }
            catch (InventoryConcurrencyException)
            {
                throw;
            }
            catch (RequestFailedException exception)
            {
                logger.LogError(exception, "Blob Storage failed while processing Stripe event {StripeEventId} (status {Status}).", stripeEventId, exception.Status);
                throw;
            }
            catch (JsonException exception)
            {
                logger.LogError(exception, "The inventory blob contains invalid JSON while processing Stripe event {StripeEventId}.", stripeEventId);
                throw;
            }
            catch (InvalidDataException exception)
            {
                logger.LogError(exception, "The inventory blob contains invalid product data while processing Stripe event {StripeEventId}.", stripeEventId);
                throw;
            }
        }

        throw new InvalidOperationException("Stripe checkout processing exhausted its update attempts.");
    }

    private List<InventoryAdjustment> ApplyCheckout(
        InventoryDocument document,
        string stripeEventId,
        IReadOnlyList<PurchasedStripeLineItem> lineItems)
    {
        var adjustments = new List<InventoryAdjustment>(lineItems.Count);

        foreach (var lineItem in lineItems)
        {
            if (lineItem.Quantity <= 0)
            {
                logger.LogWarning(
                    "Stripe event {StripeEventId} included a non-positive line-item quantity {PurchasedQuantity}. Product {StripeProductId}, price {StripePriceId} was ignored.",
                    stripeEventId,
                    lineItem.Quantity,
                    lineItem.StripeProductId,
                    lineItem.StripePriceId);
                continue;
            }

            var matches = FindStripeMatches(document, lineItem);
            if (matches.Count != 1)
            {
                logger.LogWarning(
                    "Stripe event {StripeEventId} contained an unmapped or ambiguous inventory item. Product {StripeProductId}, price {StripePriceId}, quantity {PurchasedQuantity}, match count {MatchCount}.",
                    stripeEventId,
                    lineItem.StripeProductId,
                    lineItem.StripePriceId,
                    lineItem.Quantity,
                    matches.Count);
                adjustments.Add(new InventoryAdjustment(
                    lineItem.StripeProductId,
                    lineItem.StripePriceId,
                    lineItem.Quantity,
                    null,
                    null,
                    null,
                    false,
                    false));
                continue;
            }

            var product = matches[0];
            var before = product.Quantity;
            var after = lineItem.Quantity >= before ? 0 : before - checked((int)lineItem.Quantity);
            var shortfall = lineItem.Quantity > before;
            product.Quantity = after;

            if (shortfall)
            {
                logger.LogWarning(
                    "Stripe event {StripeEventId} exceeds recorded inventory for {InventoryKey}. Product {StripeProductId}, price {StripePriceId}, requested {PurchasedQuantity}, before {QuantityBefore}; quantity was set to zero.",
                    stripeEventId,
                    product.Key,
                    lineItem.StripeProductId,
                    lineItem.StripePriceId,
                    lineItem.Quantity,
                    before);
            }

            adjustments.Add(new InventoryAdjustment(
                lineItem.StripeProductId,
                lineItem.StripePriceId,
                lineItem.Quantity,
                product.Key,
                before,
                after,
                true,
                shortfall));
        }

        return adjustments;
    }

    private static List<InventoryProduct> FindStripeMatches(
        InventoryDocument document,
        PurchasedStripeLineItem lineItem)
    {
        var priceMatches = string.IsNullOrWhiteSpace(lineItem.StripePriceId)
            ? []
            : document.Products.Where(product => string.Equals(
                product.StripePriceId,
                lineItem.StripePriceId,
                StringComparison.Ordinal)).ToList();

        if (priceMatches.Count > 0)
        {
            return priceMatches;
        }

        return string.IsNullOrWhiteSpace(lineItem.StripeProductId)
            ? []
            : document.Products.Where(product => string.Equals(
                product.StripeProductId,
                lineItem.StripeProductId,
                StringComparison.Ordinal)).ToList();
    }

    private static ProductInventoryResult CreateProductResult(
        string productId,
        IReadOnlyList<InventoryProduct> products,
        ETag eTag) =>
        new(
            productId,
            GetDisplayName(productId, products),
            products.Select(product => new InventoryVariant
            {
                VariantId = product.Size,
                Quantity = product.Quantity
            }).ToList(),
            eTag);

    private static string GetDisplayName(string productId, IReadOnlyList<InventoryProduct> products)
    {
        if (products.Count == 1)
        {
            return products[0].Name;
        }

        var separatorIndex = products[0].Name.LastIndexOf(" - ", StringComparison.Ordinal);
        return separatorIndex > 0 ? products[0].Name[..separatorIndex] : productId;
    }

    private static List<InventoryProduct> FindProducts(InventoryDocument document, string productId)
    {
        var exact = document.Products.Where(product => string.Equals(product.Key, productId, StringComparison.Ordinal)).ToList();
        return exact.Count > 0
            ? exact
            : document.Products.Where(product => product.Key.StartsWith($"{productId}-", StringComparison.Ordinal)).ToList();
    }

    private static void ValidateVariantUpdate(
        string productId,
        IReadOnlyList<InventoryProduct> products,
        IReadOnlyDictionary<string, int> quantities)
    {
        if (quantities.Count != products.Count ||
            quantities.Values.Any(quantity => quantity < 0) ||
            products.Select(product => product.Size).Distinct(StringComparer.Ordinal).Count() != products.Count ||
            products.Any(product => !quantities.ContainsKey(product.Size)) ||
            quantities.Keys.Any(variantId => string.IsNullOrWhiteSpace(variantId)))
        {
            throw new InventoryValidationException(
                $"The requested variants for product '{productId}' are invalid.");
        }
    }

    private async Task<(InventoryDocument Document, ETag ETag, BlobClient BlobClient)> DownloadAsync(CancellationToken cancellationToken)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        await EnsureContainerExistsAsync(containerClient, cancellationToken);

        var blobClient = containerClient.GetBlobClient(BlobName);
        var download = await blobClient.DownloadContentAsync(cancellationToken);
        var document = Deserialize(download.Value.Content);
        ValidateDocument(document);
        return (document, download.Value.Details.ETag, blobClient);
    }

    private static async Task<ETag> UploadAsync(
        InventoryDocument document,
        BlobClient blobClient,
        ETag expectedETag,
        CancellationToken cancellationToken)
    {
        var content = BinaryData.FromObjectAsJson(document, SerializerOptions);
        var upload = await blobClient.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                Conditions = new BlobRequestConditions { IfMatch = expectedETag }
            },
            cancellationToken);
        return upload.Value.ETag;
    }

    private async Task EnsureContainerExistsAsync(BlobContainerClient containerClient, CancellationToken cancellationToken)
    {
        try
        {
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (
            exception.Status == 409 &&
            (string.Equals(exception.ErrorCode, nameof(BlobErrorCode.ContainerAlreadyExists), StringComparison.OrdinalIgnoreCase) ||
             exception.Message.Contains("<Code>ContainerAlreadyExists</Code>", StringComparison.Ordinal)))
        {
            logger.LogDebug("The inventory container already exists.");
        }
    }

    private static InventoryDocument Deserialize(BinaryData content)
    {
        var document = JsonSerializer.Deserialize<InventoryDocument>(content, SerializerOptions)
            ?? throw new JsonException("The inventory document was empty.");

        if (document.Products is null)
        {
            throw new JsonException("The inventory document does not contain products.");
        }

        return document;
    }

    private static void ValidateDocument(InventoryDocument document)
    {
        if (document.Products.Count == 0 || document.ProcessedStripeEventIds is null)
        {
            throw new InvalidDataException("The inventory document is missing required inventory data.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var product in document.Products)
        {
            if (product is null ||
                string.IsNullOrWhiteSpace(product.Key) ||
                string.IsNullOrWhiteSpace(product.Name) ||
                string.IsNullOrWhiteSpace(product.Size) ||
                product.Quantity < 0 ||
                !keys.Add(product.Key))
            {
                throw new InvalidDataException("The inventory document contains invalid product data.");
            }
        }
    }
}
