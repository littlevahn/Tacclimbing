using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Tacc.Api.Authentication;

public static class InventoryAdminAuthorization
{
    public const string DefaultRole = "Tacc.Inventory.Admin";
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
        return !string.IsNullOrWhiteSpace(adminRole) &&
            principal.Identity?.IsAuthenticated == true &&
            principal.Claims.Any(claim =>
                (claim.Type == "roles" || claim.Type == ClaimTypes.Role) &&
                string.Equals(claim.Value, adminRole, StringComparison.Ordinal));
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
