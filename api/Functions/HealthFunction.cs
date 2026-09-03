using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Tacc.Api.Functions;

public sealed class HealthFunction(ILogger<HealthFunction> logger)
{
    [Function("Health")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")]
        HttpRequestData request)
    {
        logger.LogInformation("TACC API health endpoint called.");

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            status = "healthy",
            service = "TACC API"
        });

        return response;
    }
}
