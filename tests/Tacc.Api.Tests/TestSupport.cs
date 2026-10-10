using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tacc.Api.Authentication;

namespace Tacc.Api.Tests;

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class TestAuthentication
{
    // Synthetic test credentials only; never used by the application or local settings.
    public const string Username = "test-owner";
    public const string Password = "Synthetic test password only!";
    public static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    public static readonly string Hash = CreateHash();
    private static string CreateHash()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Password, salt, AdminPasswordHash.Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1$pbkdf2-sha256$600000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
    public static AdminAuthenticationOptions Settings() => new()
    {
        Username = Username, PasswordHash = Hash, SessionSecret = Convert.ToBase64String(Key)
    };
    public static AdminAuthenticationService Create(TestClock clock, AdminAuthenticationOptions? options = null) =>
        new(Options.Create(options ?? Settings()), clock, NullLogger<AdminAuthenticationService>.Instance);
}

internal sealed class TestFeatures : IInvocationFeatures
{
    public readonly Dictionary<Type, object> Values = [];
    public T? Get<T>() => Values.TryGetValue(typeof(T), out var value) ? (T)value : default;
    public void Set<T>(T instance) => Values[typeof(T)] = instance!;
    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => Values.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class TestRequest : HttpRequestData
{
    public TestRequest(string method = "POST", string body = "", string function = "AdminLogin") : this(CreateContext(function), method, body) { }
    private TestRequest(FunctionContext context, string method, string body) : base(context)
    {
        Method = method;
        Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var feature = new Mock<IHttpRequestDataFeature>();
        feature.Setup(x => x.GetHttpRequestDataAsync(context)).Returns(new ValueTask<HttpRequestData?>(this));
        context.Features.Set(feature.Object);
    }
    public override Stream Body { get; }
    public override HttpHeadersCollection Headers { get; } = new();
    public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = [];
    public override Uri Url { get; } = new("http://localhost:7071/api/test");
    public override IEnumerable<ClaimsIdentity> Identities { get; } = [];
    public override string Method { get; }
    public override HttpResponseData CreateResponse() => new TestResponse(FunctionContext);

    private static FunctionContext CreateContext(string name)
    {
        var context = new Mock<FunctionContext>();
        var definition = new Mock<FunctionDefinition>();
        definition.SetupGet(x => x.Name).Returns(name);
        context.SetupGet(x => x.FunctionDefinition).Returns(definition.Object);
        context.SetupProperty(x => x.Items, new Dictionary<object, object>());
        var services = new ServiceCollection().Configure<WorkerOptions>(o => o.Serializer = new JsonObjectSerializer()).BuildServiceProvider();
        context.SetupProperty(x => x.InstanceServices, services);
        var features = new TestFeatures();
        context.SetupGet(x => x.Features).Returns(features);
        // The SDK's invocation-result feature is internal. Use its native empty result holder
        // only in this harness, so tests exercise the actual middleware response assignment.
        var contract = typeof(FunctionContext).Assembly.GetType("Microsoft.Azure.Functions.Worker.Context.Features.IFunctionBindingsFeature", true)!;
        var concrete = Assembly.Load("Microsoft.Azure.Functions.Worker.Grpc")
            .GetType("Microsoft.Azure.Functions.Worker.Grpc.Features.GrpcFunctionBindingsFeature", true)!;
        features.Values[contract] = RuntimeHelpers.GetUninitializedObject(concrete);
        return context.Object;
    }

    public static string ReadBody(HttpResponseData response)
    {
        response.Body.Position = 0;
        return new StreamReader(response.Body, leaveOpen: true).ReadToEnd();
    }
}

internal sealed class TestResponse(FunctionContext context) : HttpResponseData(context)
{
    public override HttpStatusCode StatusCode { get; set; }
    public override HttpHeadersCollection Headers { get; set; } = new();
    public override Stream Body { get; set; } = new MemoryStream();
    public override HttpCookies Cookies { get; } = new Mock<HttpCookies>().Object;
}
