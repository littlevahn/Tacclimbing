using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Tacc.Api.Models.Inventory;
using Tacc.Api.Services;

namespace Tacc.Api.Functions;

public sealed class InventoryFunction(
    IInventoryService inventoryService,
    ILogger<InventoryFunction> logger)
{
    private const string ProductId = "tacc-shirt";
    private static readonly string[] SizeOrder = ["S", "M", "L", "XL"];

    [Function("Inventory")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await inventoryService.GetInventoryAsync(cancellationToken);
            var responseDto = CreateResponse(snapshot.Document);
            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(responseDto, cancellationToken);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory could not be retrieved.");

            var response = request.CreateResponse(HttpStatusCode.ServiceUnavailable);
            await response.WriteAsJsonAsync(
                new InventoryErrorResponse("Inventory is temporarily unavailable."),
                cancellationToken);
            return response;
        }
    }

    private static InventoryResponse CreateResponse(InventoryDocument document)
    {
        if (!document.Products.TryGetValue(ProductId, out var product))
        {
            throw new InvalidDataException(
                $"The inventory document does not contain product '{ProductId}'.");
        }

        var sizes = new List<InventorySizeResponse>(SizeOrder.Length);

        foreach (var size in SizeOrder)
        {
            if (!product.Sizes.TryGetValue(size, out var quantity) || quantity < 0)
            {
                throw new InvalidDataException(
                    $"The inventory quantity for size '{size}' is missing or invalid.");
            }

            sizes.Add(new InventorySizeResponse(size, quantity));
        }

        return new InventoryResponse(ProductId, sizes);
    }
}
