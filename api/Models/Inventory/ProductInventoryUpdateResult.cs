namespace Tacc.Api.Models.Inventory;

public sealed record ProductInventoryUpdateResult(
    ProductInventoryResult ProductInventory,
    IReadOnlyDictionary<string, int> PreviousQuantities);
