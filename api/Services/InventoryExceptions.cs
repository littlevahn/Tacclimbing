namespace Tacc.Api.Services;

public sealed class InventoryValidationException(string message) : Exception(message);

public sealed class InventoryConcurrencyException(string message, Exception innerException)
    : Exception(message, innerException);
