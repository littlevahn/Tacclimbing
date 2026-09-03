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
    [Function("Inventory")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/{productId}")]
        HttpRequestData request,
        string productId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await inventoryService.GetProductInventoryAsync(
                productId,
                cancellationToken);

            if (result is null)
            {
                logger.LogInformation(
                    "Inventory was requested for unknown product {ProductId}.",
                    productId);

                var notFoundResponse = request.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(
                    new InventoryErrorResponse("Product inventory was not found."),
                    cancellationToken);
                return notFoundResponse;
            }

            var responseDto = CreateResponse(result);
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

    private static ProductInventoryResponse CreateResponse(ProductInventoryResult result)
    {
        var variants = result.Variants
            .Select(variant => new VariantInventoryResponse(
                variant.VariantId,
                variant.Quantity))
            .ToList();

        return new ProductInventoryResponse(
            result.ProductId,
            result.Name,
            variants);
    }
}
