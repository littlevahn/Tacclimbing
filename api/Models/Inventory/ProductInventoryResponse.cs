using System.Text.Json.Serialization;

namespace Tacc.Api.Models.Inventory;

public sealed record ProductInventoryResponse(
    [property: JsonPropertyName("productId")] string ProductId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("variants")] IReadOnlyList<VariantInventoryResponse> Variants);

public sealed record VariantInventoryResponse(
    [property: JsonPropertyName("variantId")] string VariantId,
    [property: JsonPropertyName("quantity")] int Quantity);

public sealed record InventoryErrorResponse(
    [property: JsonPropertyName("error")] string Error);
