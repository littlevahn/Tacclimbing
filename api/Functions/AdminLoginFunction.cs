using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Tacc.Api.Authentication;
using Tacc.Api.Models.Inventory;

namespace Tacc.Api.Functions;

public sealed class AdminLoginFunction(AdminAuthenticationService authentication,
    LoginThrottle throttle, LoginSourceResolver sources)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaximumBodyBytes = 4096;

    [Function("AdminLogin")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/login")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!authentication.IsConfigured)
            return await ErrorAsync(request, HttpStatusCode.ServiceUnavailable, "Administrator sign-in is temporarily unavailable.", cancellationToken);

        using var lease = throttle.TryAcquire(sources.Resolve(request), out var retryAfter);
        if (lease is null)
        {
            var response = await ErrorAsync(request, HttpStatusCode.TooManyRequests, "Too many sign-in attempts. Please try again later.", cancellationToken);
            response.Headers.Add("Retry-After", retryAfter.ToString(CultureInfo.InvariantCulture));
            return response;
        }

        LoginRequest? login;
        try
        {
            // Bound memory and parsing work even for chunked or oversized request bodies.
            var buffer = new byte[MaximumBodyBytes + 1];
            try
            {
                var count = 0;
                while (count < buffer.Length)
                {
                    var read = await request.Body.ReadAsync(buffer.AsMemory(count), cancellationToken);
                    if (read == 0) break;
                    count += read;
                }
                if (count > MaximumBodyBytes)
                    return await ErrorAsync(request, HttpStatusCode.BadRequest, "A valid username and password are required.", cancellationToken);
                login = JsonSerializer.Deserialize<LoginRequest>(buffer.AsSpan(0, count), JsonOptions);
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer); }
        }
        catch (JsonException)
        {
            return await ErrorAsync(request, HttpStatusCode.BadRequest, "A valid username and password are required.", cancellationToken);
        }
        if (login is null || string.IsNullOrWhiteSpace(login.Username) || login.Username.Length > 128 ||
            string.IsNullOrEmpty(login.Password) || login.Password.Length > AdminPasswordHash.MaximumPasswordLength)
            return await ErrorAsync(request, HttpStatusCode.BadRequest, "A valid username and password are required.", cancellationToken);

        if (!authentication.VerifyCredentials(login.Username, login.Password))
            return await ErrorAsync(request, HttpStatusCode.Unauthorized, "Invalid username or password.", cancellationToken);

        var session = authentication.IssueSession();
        var success = request.CreateResponse(HttpStatusCode.OK);
        success.Headers.Add("Cache-Control", "no-store");
        await success.WriteAsJsonAsync(new { accessToken = session.AccessToken, expiresAt = session.ExpiresAt }, cancellationToken);
        return success;
    }

    private static async Task<HttpResponseData> ErrorAsync(HttpRequestData request, HttpStatusCode status,
        string message, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(status);
        response.Headers.Add("Cache-Control", "no-store");
        if (status == HttpStatusCode.Unauthorized) response.Headers.Add("WWW-Authenticate", "Bearer");
        await response.WriteAsJsonAsync(new InventoryErrorResponse(message), cancellationToken);
        return response;
    }

    private sealed record LoginRequest(string? Username, string? Password);
}
