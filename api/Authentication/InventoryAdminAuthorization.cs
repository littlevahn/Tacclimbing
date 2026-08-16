using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Tacc.Api.Authentication;

public static class InventoryAdminAuthorization
{
    public const string DefaultRole = "Tacc.Inventory.Admin";

    public const string DefaultScope = "Inventory.Manage";

    public const string ScopeClaimType = "scp";

    public const string MappedScopeClaimType = "http://schemas.microsoft.com/identity/claims/scope";
}

public sealed class InventoryAdminAuthorizationRequirement : IAuthorizationRequirement;

public sealed class InventoryAdminAuthorizationHandler(
    IOptions<EntraAuthenticationOptions> options)
    : AuthorizationHandler<InventoryAdminAuthorizationRequirement>
{
    public bool IsAuthorized(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var adminRole = options.Value.AdminRole;
        var adminScope = options.Value.AdminScope;
        var hasAdminRole = !string.IsNullOrWhiteSpace(adminRole) &&
            principal.Claims.Any(claim =>
                (claim.Type == "roles" || claim.Type == ClaimTypes.Role) &&
                string.Equals(claim.Value, adminRole, StringComparison.Ordinal));
        var hasAdminScope = !string.IsNullOrWhiteSpace(adminScope) &&
            principal.Claims
                .Where(claim => claim.Type is
                    InventoryAdminAuthorization.ScopeClaimType or
                    InventoryAdminAuthorization.MappedScopeClaimType)
                .SelectMany(claim => claim.Value.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Any(scope => string.Equals(scope, adminScope, StringComparison.Ordinal));

        return principal.Identity?.IsAuthenticated == true &&
            hasAdminRole &&
            hasAdminScope;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        InventoryAdminAuthorizationRequirement requirement)
    {
        if (IsAuthorized(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
