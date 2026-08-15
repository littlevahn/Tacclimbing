using Azure;

namespace Tacc.Api.Models.Inventory;

public sealed record InventorySnapshot(InventoryDocument Document, ETag ETag);
