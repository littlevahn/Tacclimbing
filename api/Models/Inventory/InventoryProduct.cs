namespace Tacc.Api.Models.Inventory;

public sealed class InventoryProduct
{
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Size { get; init; } = string.Empty;

    public string? StripeProductId { get; init; }

    public string? StripePriceId { get; init; }

    public int Quantity { get; set; }
}
