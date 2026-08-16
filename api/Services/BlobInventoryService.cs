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

        var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);

        try
        {
            await EnsureContainerExistsAsync(containerClient, cancellationToken);

            var blobClient = containerClient.GetBlobClient(BlobName);
            var download = await blobClient.DownloadContentAsync(cancellationToken);
            var document = Deserialize(download.Value.Content);

            if (!document.Products.TryGetValue(productId, out var product))
            {
                return null;
            }

            ValidateProduct(productId, product);

             var productInventoryResult = new ProductInventoryResult(
                productId,
                product,
                download.Value.Details.ETag);
            return productInventoryResult;
        }
        catch (RequestFailedException exception)
        {
            logger.LogError(
                exception,
                "Blob Storage failed while retrieving inventory (status {Status}).",
                exception.Status);
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

        var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);

        try
        {
            await EnsureContainerExistsAsync(containerClient, cancellationToken);

            var blobClient = containerClient.GetBlobClient(BlobName);
            var download = await blobClient.DownloadContentAsync(cancellationToken);
            var document = Deserialize(download.Value.Content);

            if (!document.Products.TryGetValue(productId, out var product))
            {
                return null;
            }

            ValidateProduct(productId, product);
            ValidateRequestedVariants(productId, product, quantities);

            var previousQuantities = product.Variants.ToDictionary(
                variant => variant.Key,
                variant => variant.Value.Quantity,
                StringComparer.Ordinal);

            foreach (var (variantId, quantity) in quantities)
            {
                product.Variants[variantId] = new InventoryVariant { Quantity = quantity };
            }

            var content = BinaryData.FromObjectAsJson(document, SerializerOptions);
            Response<BlobContentInfo> upload;
            try
            {
                upload = await blobClient.UploadAsync(
                    content,
                    new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                        Conditions = new BlobRequestConditions { IfMatch = new ETag(expectedETag) }
                    },
                    cancellationToken);
            }
            catch (RequestFailedException exception) when (exception.Status == 412)
            {
                throw new InventoryConcurrencyException("Inventory changed during the update.", exception);
            }

            return new ProductInventoryUpdateResult(
                new ProductInventoryResult(productId, product, upload.Value.ETag),
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
            logger.LogError(
                exception,
                "Blob Storage failed while updating inventory (status {Status}).",
                exception.Status);
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

    private async Task EnsureContainerExistsAsync(
        BlobContainerClient containerClient,
        CancellationToken cancellationToken)
    {
        try
        {
            await containerClient.CreateIfNotExistsAsync(
                PublicAccessType.None,
                cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (
            exception.Status == 409 &&
            (string.Equals(
                exception.ErrorCode,
                nameof(BlobErrorCode.ContainerAlreadyExists),
                StringComparison.OrdinalIgnoreCase) ||
             exception.Message.Contains(
                "<Code>ContainerAlreadyExists</Code>",
                StringComparison.Ordinal)))
        {
            // Some emulator versions return the correct idempotent conflict body
            // with a non-storage content type, so the SDK does not suppress it.
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

    private static void ValidateProduct(string productId, InventoryProduct product)
    {
        if (product is null ||
            string.IsNullOrWhiteSpace(product.Name) ||
            product.Variants is null)
        {
            throw new InvalidDataException(
                $"Product '{productId}' is missing required inventory data.");
        }

        foreach (var (variantId, variant) in product.Variants)
        {
            if (string.IsNullOrWhiteSpace(variantId) ||
                variant is null ||
                variant.Quantity < 0)
            {
                throw new InvalidDataException(
                    $"Product '{productId}' contains invalid variant data.");
            }
        }
    }

    private static void ValidateRequestedVariants(
        string productId,
        InventoryProduct product,
        IReadOnlyDictionary<string, int> quantities)
    {
        if (quantities.Count != product.Variants.Count ||
            quantities.Keys.Any(variantId =>
                string.IsNullOrWhiteSpace(variantId) ||
                !product.Variants.ContainsKey(variantId)) ||
            product.Variants.Keys.Any(variantId => !quantities.ContainsKey(variantId)) ||
            quantities.Values.Any(quantity => quantity < 0))
        {
            throw new InventoryValidationException(
                $"The requested variants for product '{productId}' are invalid.");
        }
    }

}
