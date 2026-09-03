namespace Tacc.Api.Models.Inventory;

public sealed record CheckoutInventoryItem(
    string InventoryKey,
    string ProductId,
    string VariantId,
    string StripePriceId,
    int Quantity);
