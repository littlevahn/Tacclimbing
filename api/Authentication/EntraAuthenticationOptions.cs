namespace Tacc.Api.Authentication;

public sealed class EntraAuthenticationOptions
{
    public const string SectionName = "Entra";

    public string? TenantId { get; init; }

    public string? ClientId { get; init; }

    public string? Authority { get; init; }

    public string? Audience { get; init; }

    public string AdminRole { get; init; } = InventoryAdminAuthorization.DefaultRole;
}
