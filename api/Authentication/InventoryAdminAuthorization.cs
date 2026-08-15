using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Tacc.Api.Authentication;

public static class InventoryAdminAuthorization
{
    public const string PolicyName = "InventoryAdministrator";
    public const string DefaultRole = "Tacc.Inventory.Admin";
}

public sealed class InventoryAdminAuthorizationRequirement : IAuthorizationRequirement;

public sealed class InventoryAdminAuthorizationHandler(
    IOptions<EntraAuthenticationOptions> options)
    : AuthorizationHandler<InventoryAdminAuthorizationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        InventoryAdminAuthorizationRequirement requirement)
    {
        var adminRole = options.Value.AdminRole;
        if (!string.IsNullOrWhiteSpace(adminRole) &&
            context.User.Claims.Any(claim =>
                (claim.Type == "roles" || claim.Type == ClaimTypes.Role) &&
                string.Equals(claim.Value, adminRole, StringComparison.Ordinal)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
