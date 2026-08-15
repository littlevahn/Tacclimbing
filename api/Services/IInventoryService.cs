using Tacc.Api.Models.Inventory;

namespace Tacc.Api.Services;

public interface IInventoryService
{
    Task<InventorySnapshot> GetInventoryAsync(CancellationToken cancellationToken = default);
}
