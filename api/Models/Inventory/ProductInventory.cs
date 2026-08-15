namespace Tacc.Api.Models.Inventory;

public sealed class ProductInventory
{
    public Dictionary<string, int> Sizes { get; init; } =
        new(StringComparer.Ordinal);
}
