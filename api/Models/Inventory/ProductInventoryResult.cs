using Azure;

namespace Tacc.Api.Models.Inventory;

public sealed record ProductInventoryResult(
    string ProductId,
    string Name,
    IReadOnlyList<InventoryVariant> Variants,
    ETag ETag);
