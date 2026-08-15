namespace Tacc.Api.Models.Inventory;

public sealed class InventoryProduct
{
    public string Name { get; init; } = string.Empty;

    public Dictionary<string, InventoryVariant> Variants { get; init; } =
        new(StringComparer.Ordinal);
}
