using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Tacc.Api.Models.Inventory;
using Tacc.Api.Services;

namespace Tacc.Api.Functions;

public sealed class AdminInventoryFunction(
    IInventoryService inventoryService,
    ILogger<AdminInventoryFunction> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Function("AdminInventory")]
    public async Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/inventory/{productId}")]
        HttpRequestData request,
        string productId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await inventoryService.GetProductInventoryAsync(productId, cancellationToken);
            if (result is null)
            {
                return await WriteErrorAsync(request, HttpStatusCode.NotFound,
                    "Product inventory was not found.", cancellationToken);
            }

            return await WriteJsonAsync(request, HttpStatusCode.OK, CreateResponse(result), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Admin inventory could not be retrieved for {ProductId}.", productId);
            return await WriteErrorAsync(request, HttpStatusCode.ServiceUnavailable,
                "Inventory is temporarily unavailable.", cancellationToken);
        }
    }

    [Function("AdminInventoryUpdate")]
    public async Task<HttpResponseData> PutAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "admin/inventory/{productId}")]
        HttpRequestData request,
        string productId,
        FunctionContext functionContext,
        CancellationToken cancellationToken)
    {
        AdminInventoryUpdateRequest? updateRequest;
        try
        {
            updateRequest = await JsonSerializer.DeserializeAsync<AdminInventoryUpdateRequest>(
                request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return await WriteErrorAsync(request, HttpStatusCode.BadRequest,
                "Inventory quantities must be non-negative integers.", cancellationToken);
        }

        if (!TryGetValidRequest(updateRequest, out var etag, out var quantities))
        {
            return await WriteErrorAsync(request, HttpStatusCode.BadRequest,
                "Inventory quantities must be non-negative integers and require a valid ETag.", cancellationToken);
        }

        try
        {
            var update = await inventoryService.UpdateProductInventoryAsync(
                productId, etag, quantities, cancellationToken);
            if (update is null)
            {
                return await WriteErrorAsync(request, HttpStatusCode.NotFound,
                    "Product inventory was not found.", cancellationToken);
            }

            LogSuccessfulUpdate(functionContext, productId, update.PreviousQuantities, quantities);
            return await WriteJsonAsync(request, HttpStatusCode.OK,
                CreateResponse(update.ProductInventory), cancellationToken);
        }
        catch (InventoryValidationException)
        {
            return await WriteErrorAsync(request, HttpStatusCode.BadRequest,
                "Submitted variants must exactly match the existing product variants.", cancellationToken);
        }
        catch (InventoryConcurrencyException)
        {
            return await WriteErrorAsync(request, HttpStatusCode.Conflict,
                "Inventory changed since it was loaded. Refresh and try again.", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Admin inventory update failed for {ProductId}.", productId);
            return await WriteErrorAsync(request, HttpStatusCode.ServiceUnavailable,
                "Inventory is temporarily unavailable.", cancellationToken);
        }
    }

    private static bool TryGetValidRequest(
        AdminInventoryUpdateRequest? request,
        out string etag,
        out IReadOnlyDictionary<string, int> quantities)
    {
        etag = request?.ETag?.Trim() ?? string.Empty;
        quantities = new Dictionary<string, int>(StringComparer.Ordinal);

        if (request?.Variants is null || request.Variants.Count == 0 ||
            etag.Length < 3 || etag[0] != '"' || etag[^1] != '"')
        {
            return false;
        }

        var parsedQuantities = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (variantId, quantityElement) in request.Variants)
        {
            if (string.IsNullOrWhiteSpace(variantId) ||
                quantityElement.ValueKind != JsonValueKind.Number ||
                !quantityElement.TryGetInt32(out var quantity) ||
                quantity < 0)
            {
                return false;
            }

            parsedQuantities[variantId] = quantity;
        }

        quantities = parsedQuantities;
        return true;
    }

    private void LogSuccessfulUpdate(
        FunctionContext functionContext,
        string productId,
        IReadOnlyDictionary<string, int> previousQuantities,
        IReadOnlyDictionary<string, int> newQuantities)
    {
        var principal = functionContext.Items.TryGetValue("AdminPrincipal", out var identity) 
            ? identity as ClaimsPrincipal
            : null;
        var userId = principal?.FindFirst("oid")?.Value ?? principal?.FindFirst("sub")?.Value ?? "unknown";
        var changes = previousQuantities.ToDictionary(
            entry => entry.Key,
            entry => new { Previous = entry.Value, Current = newQuantities[entry.Key] });

        logger.LogInformation(
            "Inventory updated by admin {AdminUserId} for {ProductId}. Changes: {@InventoryChanges}. InvocationId: {InvocationId}",
            userId,
            productId,
            changes,
            functionContext.InvocationId);
    }

    private static AdminProductInventoryResponse CreateResponse(ProductInventoryResult result) =>
        new(
            result.ProductId,
            result.Product.Name,
            result.ETag.ToString(),
            result.Product.Variants.Select(variant => new VariantInventoryResponse(
                variant.Key,
                variant.Value.Quantity)).ToList());

    private static async Task<HttpResponseData> WriteJsonAsync(
        HttpRequestData request,
        HttpStatusCode statusCode,
        object body,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(body, cancellationToken);
        return response;
    }

    private static Task<HttpResponseData> WriteErrorAsync(
        HttpRequestData request,
        HttpStatusCode statusCode,
        string error,
        CancellationToken cancellationToken) =>
        WriteJsonAsync(request, statusCode, new InventoryErrorResponse(error), cancellationToken);
}
