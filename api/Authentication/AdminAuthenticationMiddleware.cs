using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Tacc.Api.Models.Inventory;

namespace Tacc.Api.Authentication;

public sealed class AdminAuthenticationMiddleware(AdminAuthenticationService authentication) : IFunctionsWorkerMiddleware
{
    public static bool RequiresAuthentication(string functionName) =>
        functionName is "AdminInventory" or "AdminInventoryUpdate";

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (!RequiresAuthentication(context.FunctionDefinition.Name))
        {
            await next(context);
            return;
        }

        var request = await context.GetHttpRequestDataAsync();
        if (request is null)
        {
            throw new InvalidOperationException("A protected admin function requires an HTTP request.");
        }

        if (!authentication.IsConfigured)
        {
            context.GetInvocationResult().Value = await CreateErrorResponseAsync(request,
                System.Net.HttpStatusCode.ServiceUnavailable, "Administrator access is temporarily unavailable.", context.CancellationToken);
            return;
        }

        var headers = request.Headers.TryGetValues("Authorization", out var values) ? values.ToArray() : [];
        var principal = authentication.ValidateBearer(headers.Length == 1 ? headers[0] : null);

        if (principal?.Identity?.IsAuthenticated != true)
        {
            context.GetInvocationResult().Value = await CreateErrorResponseAsync(
                request,
                System.Net.HttpStatusCode.Unauthorized,
                "Authentication is required.",
                context.CancellationToken);
            return;
        }

        // Add no-store to successful admin responses as well as errors.
        context.Items["AdminPrincipal"] = principal;
        await next(context);
        if (context.GetInvocationResult().Value is HttpResponseData response)
            response.Headers.Add("Cache-Control", "no-store");
    }

    private static async Task<HttpResponseData> CreateErrorResponseAsync(
        HttpRequestData request,
        System.Net.HttpStatusCode statusCode,
        string error,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Cache-Control", "no-store");
        if (statusCode == System.Net.HttpStatusCode.Unauthorized) response.Headers.Add("WWW-Authenticate", "Bearer");
        await response.WriteAsJsonAsync(new InventoryErrorResponse(error), cancellationToken);
        return response;
    }
}
