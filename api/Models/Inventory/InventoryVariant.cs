namespace Tacc.Api.Models.Inventory;

public sealed class InventoryVariant
{
    public string VariantId { get; init; } = string.Empty;

    public int Quantity { get; init; }
}
