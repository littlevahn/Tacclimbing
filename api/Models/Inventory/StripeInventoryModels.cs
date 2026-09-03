namespace Tacc.Api.Models.Inventory;

public sealed record PurchasedStripeLineItem(
    string? StripeProductId,
    string? StripePriceId,
    long Quantity);

public sealed record InventoryAdjustment(
    string? StripeProductId,
    string? StripePriceId,
    long PurchasedQuantity,
    string? InventoryKey,
    int? QuantityBefore,
    int? QuantityAfter,
    bool IsMapped,
    bool WasShortfall);

public sealed record StripeInventoryUpdateResult(
    bool IsDuplicate,
    IReadOnlyList<InventoryAdjustment> Adjustments);
