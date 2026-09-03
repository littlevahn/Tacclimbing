namespace Tacc.Api.Models.Inventory;

public sealed class InventoryDocument
{
    public List<InventoryProduct> Products { get; init; } = [];

    // Kept in the same conditional blob write as a checkout's quantity changes.
    // This makes the inventory document the durable idempotency boundary.
    public HashSet<string> ProcessedStripeEventIds { get; init; } =
        new(StringComparer.Ordinal);
}
