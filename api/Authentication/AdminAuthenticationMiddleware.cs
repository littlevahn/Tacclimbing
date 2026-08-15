using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Tacc.Api.Models.Inventory;

namespace Tacc.Api.Authentication;

public sealed class AdminAuthenticationMiddleware(
    IEntraTokenValidator tokenValidator,
    IAuthorizationService authorizationService) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (!context.FunctionDefinition.Name.StartsWith("AdminInventory", StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        var request = await context.GetHttpRequestDataAsync();
        if (request is null)
        {
            await next(context);
            return;
        }

        var principal = await tokenValidator.ValidateAsync(
            request.Headers.TryGetValues("Authorization", out var values)
                ? values.FirstOrDefault()
                : null,
            context.CancellationToken);

        if (principal?.Identity?.IsAuthenticated != true)
        {
            context.GetInvocationResult().Value = await CreateErrorResponseAsync(
                request,
                System.Net.HttpStatusCode.Unauthorized,
                "Authentication is required.",
                context.CancellationToken);
            return;
        }

        var authorization = await authorizationService.AuthorizeAsync(
            principal,
            resource: null,
            InventoryAdminAuthorization.PolicyName);
        if (!authorization.Succeeded)
        {
            context.GetInvocationResult().Value = await CreateErrorResponseAsync(
                request,
                System.Net.HttpStatusCode.Forbidden,
                "Inventory administrator access is required.",
                context.CancellationToken);
            return;
        }

        context.Items["AdminPrincipal"] = principal;
        await next(context);
    }

    private static async Task<HttpResponseData> CreateErrorResponseAsync(
        HttpRequestData request,
        System.Net.HttpStatusCode statusCode,
        string error,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new InventoryErrorResponse(error), cancellationToken);
        return response;
    }
}
