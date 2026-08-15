using Azure;

namespace Tacc.Api.Models.Inventory;

public sealed record ProductInventoryResult(
    string ProductId,
    InventoryProduct Product,
    ETag ETag);
