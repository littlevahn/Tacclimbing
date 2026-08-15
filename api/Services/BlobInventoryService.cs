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

    public async Task<InventorySnapshot> GetInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);

        try
        {
            await containerClient.CreateIfNotExistsAsync(
                PublicAccessType.None,
                cancellationToken: cancellationToken);

            var blobClient = containerClient.GetBlobClient(BlobName);
            var download = await DownloadOrInitializeAsync(blobClient, cancellationToken);
            var document = Deserialize(download.Value.Content);

            return new InventorySnapshot(document, download.Value.Details.ETag);
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
    }

    private async Task<Response<BlobDownloadResult>> DownloadOrInitializeAsync(
        BlobClient blobClient,
        CancellationToken cancellationToken)
    {
        try
        {
            return await blobClient.DownloadContentAsync(cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            var defaultInventory = CreateDefaultInventory();
            var content = BinaryData.FromObjectAsJson(defaultInventory, SerializerOptions);

            try
            {
                await blobClient.UploadAsync(
                    content,
                    new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders
                        {
                            ContentType = "application/json"
                        },
                        Conditions = new BlobRequestConditions
                        {
                            IfNoneMatch = ETag.All
                        }
                    },
                    cancellationToken);

                logger.LogInformation(
                    "Initialized inventory because {BlobName} did not exist.",
                    BlobName);
            }
            catch (RequestFailedException uploadException)
                when (uploadException.Status is 409 or 412)
            {
                // Another request initialized the blob after this request observed it missing.
            }

            return await blobClient.DownloadContentAsync(cancellationToken);
        }
    }

    private static InventoryDocument Deserialize(BinaryData content)
    {
        return JsonSerializer.Deserialize<InventoryDocument>(content, SerializerOptions)
            ?? throw new JsonException("The inventory document was empty.");
    }

    private static InventoryDocument CreateDefaultInventory()
    {
        return new InventoryDocument
        {
            Products = new Dictionary<string, ProductInventory>(StringComparer.Ordinal)
            {
                ["tacc-shirt"] = new ProductInventory
                {
                    Sizes = new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["S"] = 0,
                        ["M"] = 0,
                        ["L"] = 0,
                        ["XL"] = 0
                    }
                }
            }
        };
    }
}
