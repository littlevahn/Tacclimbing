using Tacc.Api.Models.Inventory;

namespace Tacc.Api.Services;

public interface IInventoryService
{
    Task<ProductInventoryResult?> GetProductInventoryAsync(
        string productId,
        CancellationToken cancellationToken = default);

    Task<ProductInventoryUpdateResult?> UpdateProductInventoryAsync(
        string productId,
        string expectedETag,
        IReadOnlyDictionary<string, int> quantities,
        CancellationToken cancellationToken = default);

    Task<StripeInventoryUpdateResult> ProcessStripeCheckoutAsync(
        string stripeEventId,
        IReadOnlyList<PurchasedStripeLineItem> lineItems,
        CancellationToken cancellationToken = default);
}
