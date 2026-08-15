using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tacc.Api.Models.Inventory;

public sealed record AdminProductInventoryResponse(
    [property: JsonPropertyName("productId")] string ProductId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("etag")] string ETag,
    [property: JsonPropertyName("variants")] IReadOnlyList<VariantInventoryResponse> Variants);

public sealed record AdminInventoryUpdateRequest(
    [property: JsonPropertyName("etag")] string? ETag,
    [property: JsonPropertyName("variants")] Dictionary<string, JsonElement>? Variants);
