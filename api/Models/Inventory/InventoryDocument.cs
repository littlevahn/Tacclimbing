namespace Tacc.Api.Models.Inventory;

public sealed class InventoryDocument
{
    public Dictionary<string, ProductInventory> Products { get; init; } =
        new(StringComparer.Ordinal);
}
