using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.IdentityModel.Tokens;
using Tacc.Api.Authentication;
using Tacc.Api.Functions;

namespace Tacc.Api.Tests;

public sealed class AuthenticationTests
{
    private static AdminLoginFunction Login(TestClock clock, AdminAuthenticationService? auth = null) =>
        new(auth ?? TestAuthentication.Create(clock), new LoginThrottle(clock), new LoginSourceResolver(false));

    [Fact]
    public async Task CorrectCredentialsIssueAnExactlyEightHourAuthenticatedSession()
    {
        var clock = new TestClock();
        var auth = TestAuthentication.Create(clock);
        var response = await Login(clock, auth).RunAsync(new TestRequest(body: JsonSerializer.Serialize(new
        { username = TestAuthentication.Username, password = TestAuthentication.Password })), default);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.GetValues("Cache-Control").Single());
        using var payload = JsonDocument.Parse(TestRequest.ReadBody(response));
        var token = payload.RootElement.GetProperty("accessToken").GetString()!;
        Assert.Equal(clock.Now.AddHours(8), payload.RootElement.GetProperty("expiresAt").GetDateTimeOffset());
        var principal = auth.ValidateBearer("Bearer " + token);
        Assert.True(principal?.Identity?.IsAuthenticated);
        Assert.Equal(TestAuthentication.Username, principal!.Identity!.Name);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);
        Assert.Equal(TimeSpan.FromHours(8), jwt.ValidTo - jwt.IssuedAt);
        clock.Now = clock.Now.AddHours(8);
        Assert.Null(auth.ValidateBearer("Bearer " + token));
    }

    [Fact]
    public async Task WrongUserAndWrongPasswordHaveIdenticalGenericResponses()
    {
        var login = Login(new TestClock());
        var wrongUser = await login.RunAsync(new TestRequest(body: JsonSerializer.Serialize(new
        { username = "unknown", password = TestAuthentication.Password })), default);
        var wrongPassword = await login.RunAsync(new TestRequest(body: JsonSerializer.Serialize(new
        { username = TestAuthentication.Username, password = "incorrect" })), default);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongUser.StatusCode);
        Assert.Equal(wrongUser.StatusCode, wrongPassword.StatusCode);
        Assert.Equal(TestRequest.ReadBody(wrongUser), TestRequest.ReadBody(wrongPassword));
        Assert.DoesNotContain(TestAuthentication.Password, TestRequest.ReadBody(wrongUser));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"username\":42,\"password\":\"test\"}")]
    [InlineData("{\"username\":\"owner\",\"password\":\"\"}")]
    [InlineData("[]")]
    public async Task MalformedLoginReturns400(string body)
    {
        var response = await Login(new TestClock()).RunAsync(new TestRequest(body: body), default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("no-store", response.Headers.GetValues("Cache-Control").Single());
    }

    [Fact]
    public async Task OversizedBodyAndPasswordAreRejected()
    {
        var login = Login(new TestClock());
        foreach (var body in new[] { new string(' ', 4097), JsonSerializer.Serialize(new { username = "owner", password = new string('p', 257) }) })
            Assert.Equal(HttpStatusCode.BadRequest, (await login.RunAsync(new TestRequest(body: body), default)).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-key")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-user")]
    [InlineData("wrong-algorithm")]
    [InlineData("unsigned")]
    [InlineData("missing-expiration")]
    [InlineData("wrong-duration")]
    public void InvalidTokensAreRejected(string kind)
    {
        var clock = new TestClock();
        var now = clock.Now.UtcDateTime;
        var issued = kind == "expired" ? now.AddHours(-9) : now;
        var signing = kind == "unsigned" ? null : new SigningCredentials(
            new SymmetricSecurityKey(kind is "wrong-key" or "wrong-algorithm" ? RandomNumberGenerator.GetBytes(64) : TestAuthentication.Key),
            kind == "wrong-algorithm" ? SecurityAlgorithms.HmacSha512 : SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(kind == "wrong-issuer" ? "other" : AdminAuthenticationService.Issuer,
            kind == "wrong-audience" ? "other" : AdminAuthenticationService.Audience,
            [new Claim("sub", kind == "wrong-user" ? "other" : TestAuthentication.Username),
             new Claim("iat", new DateTimeOffset(issued).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)],
            issued, kind == "missing-expiration" ? null : issued.AddHours(kind == "wrong-duration" ? 9 : 8), signing);
        Assert.Null(TestAuthentication.Create(clock).ValidateBearer("Bearer " + new JwtSecurityTokenHandler().WriteToken(jwt)));
    }

    [Fact]
    public void ModifiedAndMalformedTokensAreRejected()
    {
        var auth = TestAuthentication.Create(new TestClock());
        var token = auth.IssueSession().AccessToken;
        var parts = token.Split('.');
        parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        foreach (var header in new[] { "Bearer " + string.Join('.', parts), "Bearer nonsense", "Basic abc", "Bearer ", "Bearer " + new string('x', 5000) })
            Assert.Null(auth.ValidateBearer(header));
    }

    [Theory]
    [InlineData("username")]
    [InlineData("hash")]
    [InlineData("iterations")]
    [InlineData("secret")]
    [InlineData("short-secret")]
    public async Task InvalidConfigurationDisablesOnlyAdminEndpoints(string invalid)
    {
        var settings = TestAuthentication.Settings();
        switch (invalid)
        {
            case "username": settings.Username = null; break;
            case "hash": settings.PasswordHash = "bad"; break;
            case "iterations": settings.PasswordHash = settings.PasswordHash!.Replace("600000", "1"); break;
            case "secret": settings.SessionSecret = null; break;
            case "short-secret": settings.SessionSecret = Convert.ToBase64String(new byte[16]); break;
        }
        var clock = new TestClock();
        var auth = TestAuthentication.Create(clock, settings);
        Assert.False(auth.IsConfigured);
        Assert.Null(auth.ValidateBearer("Bearer anything"));
        var login = await Login(clock, auth).RunAsync(new TestRequest(), default);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, login.StatusCode);
        foreach (var name in new[] { "AdminInventory", "AdminInventoryUpdate" })
        {
            var request = new TestRequest(function: name);
            await new AdminAuthenticationMiddleware(auth).Invoke(request.FunctionContext, _ => throw new Exception("Must not execute"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, Assert.IsAssignableFrom<HttpResponseData>(request.FunctionContext.GetInvocationResult().Value).StatusCode);
        }
        foreach (var name in new[] { "Inventory", "Health", "StripeCheckout", "StripeWebhook" })
        {
            var called = false;
            await new AdminAuthenticationMiddleware(auth).Invoke(new TestRequest(function: name).FunctionContext, _ => { called = true; return Task.CompletedTask; });
            Assert.True(called);
        }
    }

    [Theory]
    [InlineData("AdminInventory", "GET")]
    [InlineData("AdminInventoryUpdate", "PUT")]
    public async Task BothAdminRoutesRequireAValidBearerToken(string name, string method)
    {
        var auth = TestAuthentication.Create(new TestClock());
        var middleware = new AdminAuthenticationMiddleware(auth);
        foreach (var header in new[] { "", "Bearer invalid", "Bearer " + auth.IssueSession().AccessToken })
        {
            var request = new TestRequest(method, function: name);
            if (header != "") request.Headers.Add("Authorization", header);
            var called = false;
            await middleware.Invoke(request.FunctionContext, context =>
            {
                called = true;
                Assert.Equal(TestAuthentication.Username, Assert.IsType<ClaimsPrincipal>(context.Items["AdminPrincipal"]).Identity!.Name);
                context.GetInvocationResult().Value = request.CreateResponse(HttpStatusCode.OK);
                return Task.CompletedTask;
            });
            var response = Assert.IsAssignableFrom<HttpResponseData>(request.FunctionContext.GetInvocationResult().Value);
            Assert.Equal(header == "" || header == "Bearer invalid" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(header != "" && header != "Bearer invalid", called);
            Assert.Equal("no-store", response.Headers.GetValues("Cache-Control").Single());
        }
    }

    [Fact]
    public void EveryAdminInventoryTriggerHasExplicitMiddlewareProtection()
    {
        var inventoryTriggers = typeof(AdminInventoryFunction).GetMethods()
            .Where(method => method.GetParameters().Any(parameter =>
                parameter.GetCustomAttributes(typeof(HttpTriggerAttribute), false)
                    .OfType<HttpTriggerAttribute>().Any(trigger => trigger.Route?.StartsWith("admin/inventory/", StringComparison.Ordinal) == true)))
            .ToArray();
        Assert.Equal(2, inventoryTriggers.Length);
        foreach (var method in inventoryTriggers)
        {
            var attribute = Assert.Single(method.GetCustomAttributes(typeof(FunctionAttribute), false).OfType<FunctionAttribute>());
            Assert.True(AdminAuthenticationMiddleware.RequiresAuthentication(attribute.Name));
        }
        Assert.False(AdminAuthenticationMiddleware.RequiresAuthentication("AdminLogin"));
    }

    [Fact]
    public async Task MissingRequestAndExtractionErrorsFailClosed()
    {
        var request = new TestRequest(function: "AdminInventoryUpdate");
        var feature = new Moq.Mock<IHttpRequestDataFeature>();
        feature.Setup(x => x.GetHttpRequestDataAsync(request.FunctionContext)).Returns(new ValueTask<HttpRequestData?>((HttpRequestData?)null));
        request.FunctionContext.Features.Set(feature.Object);
        var middleware = new AdminAuthenticationMiddleware(TestAuthentication.Create(new TestClock()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(request.FunctionContext, _ => throw new Exception("Must not execute")));
        feature.Setup(x => x.GetHttpRequestDataAsync(request.FunctionContext)).Throws(new InvalidOperationException("Cannot extract"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(request.FunctionContext, _ => throw new Exception("Must not execute")));
    }

    [Fact]
    public async Task LoginThrottlesAndRecoversAfterAMinute()
    {
        var clock = new TestClock();
        var login = Login(clock);
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await login.RunAsync(new TestRequest(body: "{}"), default)).StatusCode);
        var rejected = await login.RunAsync(new TestRequest(body: "{}"), default);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("60", rejected.Headers.GetValues("Retry-After").Single());
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Equal(HttpStatusCode.BadRequest, (await login.RunAsync(new TestRequest(body: "{}"), default)).StatusCode);
    }

    [Fact]
    public void LimiterBoundsSourcesAndConcurrentChecksAndReleasesLeases()
    {
        var clock = new TestClock();
        var limiter = new LoginThrottle(clock);
        using (var one = limiter.TryAcquire("one", out _))
        using (var two = limiter.TryAcquire("two", out _))
            Assert.Null(limiter.TryAcquire("three", out _));
        using (var available = limiter.TryAcquire("four", out _)) Assert.NotNull(available);
        for (var i = 4; i < LoginThrottle.MaximumSources; i++)
            using (var lease = limiter.TryAcquire("source-" + i, out _)) Assert.NotNull(lease);
        Assert.Null(limiter.TryAcquire("overflow", out _));
        clock.Now = clock.Now.AddMinutes(1);
        using var recovered = limiter.TryAcquire("overflow", out _);
        Assert.NotNull(recovered);
    }

    [Theory]
    [InlineData("203.0.113.10:54321", "203.0.113.10")]
    [InlineData("[2001:db8::1]:1234", "2001:db8::1")]
    [InlineData("bad,203.0.113.10", "unknown-source")]
    public void OnlyAzurePlatformStampedClientIpIsUsed(string value, string expected)
    {
        var request = new TestRequest();
        request.Headers.Add("CLIENT-IP", value);
        request.Headers.Add("X-Forwarded-For", "spoofed");
        Assert.Equal(expected, new LoginSourceResolver(true).Resolve(request));
        Assert.Equal("unknown-source", new LoginSourceResolver(false).Resolve(request));
        var forwardedOnly = new TestRequest();
        forwardedOnly.Headers.Add("X-Forwarded-For", "203.0.113.10");
        Assert.Equal("unknown-source", new LoginSourceResolver(true).Resolve(forwardedOnly));
    }
}
