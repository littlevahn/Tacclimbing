namespace Tacc.Api.Models.Inventory;

public sealed class InventoryDocument
{
    public Dictionary<string, InventoryProduct> Products { get; init; } =
        new(StringComparer.Ordinal);
}
