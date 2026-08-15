using Tacc.Api.Models.Inventory;

namespace Tacc.Api.Services;

public interface IInventoryService
{
    Task<ProductInventoryResult?> GetProductInventoryAsync(
        string productId,
        CancellationToken cancellationToken = default);
}
