using System.Text.Json.Serialization;

namespace Tacc.Api.Models.Inventory;

public sealed record InventoryResponse(
    [property: JsonPropertyName("productId")] string ProductId,
    [property: JsonPropertyName("sizes")] IReadOnlyList<InventorySizeResponse> Sizes);

public sealed record InventorySizeResponse(
    [property: JsonPropertyName("size")] string Size,
    [property: JsonPropertyName("quantity")] int Quantity);

public sealed record InventoryErrorResponse(
    [property: JsonPropertyName("error")] string Error);
