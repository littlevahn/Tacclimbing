using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Stripe;
using Tacc.Api.Authentication;
using Tacc.Api.Functions;
using Tacc.Api.Models.Inventory;
using Tacc.Api.Services;
using Tacc.Api.Stripe;

namespace Tacc.Api.Tests;

public sealed class InventoryAndStripeTests
{
    [Fact]
    public async Task AuthenticatedUpdatePreservesContractAndStaleWriteCannotOverwrite()
    {
        var store = new TestBlobStore();
        var service = store.Service();
        var function = new AdminInventoryFunction(service, NullLogger<AdminInventoryFunction>.Instance);
        var auth = TestAuthentication.Create(new TestClock());
        var original = await service.GetProductInventoryAsync("tacc-shirt");
        var body = JsonSerializer.Serialize(new { etag = original!.ETag.ToString(), variants = new { S = 11, M = 8 } });
        async Task<HttpResponseData> Update()
        {
            var request = new TestRequest("PUT", body, "AdminInventoryUpdate");
            request.Headers.Add("Authorization", "Bearer " + auth.IssueSession().AccessToken);
            await new AdminAuthenticationMiddleware(auth).Invoke(request.FunctionContext, async context =>
                context.GetInvocationResult().Value = await function.PutAsync(request, "tacc-shirt", context, default));
            return Assert.IsAssignableFrom<HttpResponseData>(request.FunctionContext.GetInvocationResult().Value);
        }
        var saved = await Update();
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var response = JsonDocument.Parse(TestRequest.ReadBody(saved));
        Assert.Equal("tacc-shirt", response.RootElement.GetProperty("productId").GetString());
        Assert.Equal("TACC Shirt", response.RootElement.GetProperty("name").GetString());
        Assert.NotEqual(original.ETag.ToString(), response.RootElement.GetProperty("etag").GetString());
        Assert.Equal(2, response.RootElement.GetProperty("variants").GetArrayLength());
        Assert.Equal(original.ETag, store.LastIfMatch);
        var newest = store.Content;
        var stale = await Update();
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(newest, store.Content);
        Assert.Equal(1, store.SuccessfulWrites);
        Assert.Equal(11, (await service.GetProductInventoryAsync("tacc-shirt"))!.Variants.Single(v => v.VariantId == "S").Quantity);
    }

    [Fact]
    public async Task PublicInventoryAndHealthRemainAccessibleWithMissingAuthConfiguration()
    {
        var service = new TestBlobStore().Service();
        var auth = TestAuthentication.Create(new TestClock(), new AdminAuthenticationOptions());
        var request = new TestRequest("GET", function: "Inventory");
        await new AdminAuthenticationMiddleware(auth).Invoke(request.FunctionContext, async context =>
            context.GetInvocationResult().Value = await new InventoryFunction(service, NullLogger<InventoryFunction>.Instance).RunAsync(request, "tacc-shirt", default));
        var response = Assert.IsAssignableFrom<HttpResponseData>(request.FunctionContext.GetInvocationResult().Value);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(TestRequest.ReadBody(response));
        Assert.False(payload.RootElement.TryGetProperty("etag", out _));
        var health = new TestRequest("GET", function: "Health");
        await new AdminAuthenticationMiddleware(auth).Invoke(health.FunctionContext, async context =>
            context.GetInvocationResult().Value = await new HealthFunction(NullLogger<HealthFunction>.Instance).RunAsync(health));
        Assert.Equal(HttpStatusCode.OK, Assert.IsAssignableFrom<HttpResponseData>(health.FunctionContext.GetInvocationResult().Value).StatusCode);
    }

    [Fact]
    public async Task StripeAdjustmentsAreIdempotentAndRetryAConditionalConflict()
    {
        var store = new TestBlobStore { ConflictsRemaining = 1 };
        var service = store.Service();
        PurchasedStripeLineItem[] items = [new("prod_small", "price_small", 2)];
        var first = await service.ProcessStripeCheckoutAsync("evt_test", items);
        Assert.False(first.IsDuplicate);
        Assert.Equal(2, store.UploadAttempts);
        Assert.Equal(1, store.SuccessfulWrites);
        Assert.Equal(3, (await service.GetProductInventoryAsync("tacc-shirt"))!.Variants[0].Quantity);
        var duplicate = await service.ProcessStripeCheckoutAsync("evt_test", items);
        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(1, store.SuccessfulWrites);
        // Manual writes preserve the webhook's idempotency metadata and other variants.
        var current = (await service.GetProductInventoryAsync("tacc-shirt"))!;
        await service.UpdateProductInventoryAsync("tacc-shirt", current.ETag.ToString(), new Dictionary<string, int> { ["S"] = 10, ["M"] = 7 });
        Assert.True((await service.ProcessStripeCheckoutAsync("evt_test", items)).IsDuplicate);
        Assert.Equal(10, (await service.GetProductInventoryAsync("tacc-shirt"))!.Variants[0].Quantity);
    }

    [Fact]
    public async Task StripeRejectsInvalidSignaturesAndAcceptsSignedEventsWithoutAdminLogin()
    {
        const string secret = "synthetic-webhook-secret";
        var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
        var function = new StripeWebhookFunction(inventory.Object, Options.Create(new StripeOptions { WebhookSecret = secret }), NullLogger<StripeWebhookFunction>.Instance);
        var auth = TestAuthentication.Create(new TestClock(), new AdminAuthenticationOptions());
        var payload = JsonSerializer.Serialize(new { id = "evt_synthetic", @object = "event", api_version = StripeConfiguration.ApiVersion, type = "customer.created", data = new { @object = new { id = "cus_test", @object = "customer" } } });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        foreach (var valid in new[] { false, true })
        {
            var request = new TestRequest("POST", payload, "StripeWebhook");
            request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={(valid ? signature : new string('0', 64))}");
            await new AdminAuthenticationMiddleware(auth).Invoke(request.FunctionContext, async context =>
                context.GetInvocationResult().Value = await function.RunAsync(request, default));
            Assert.Equal(valid ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
                Assert.IsAssignableFrom<HttpResponseData>(request.FunctionContext.GetInvocationResult().Value).StatusCode);
        }
        inventory.VerifyNoOtherCalls();
    }
}

/// <summary>Isolated Blob SDK double that enforces the actual upload's If-Match condition.
/// No connection to the developer's inventory or production storage.</summary>
internal sealed class TestBlobStore
{
    public string Content { get; private set; } = """
        {"products":[
          {"key":"tacc-shirt-small","name":"TACC Shirt - Small","size":"S","quantity":5,"stripeProductId":"prod_small","stripePriceId":"price_small"},
          {"key":"tacc-shirt-medium","name":"TACC Shirt - Medium","size":"M","quantity":7,"stripeProductId":"prod_medium","stripePriceId":"price_medium"}
        ],"processedStripeEventIds":[]}
        """;
    private int version = 1;
    public int SuccessfulWrites { get; private set; }
    public int UploadAttempts { get; private set; }
    public int ConflictsRemaining { get; set; }
    public ETag LastIfMatch { get; private set; }
    public BlobInventoryService Service()
    {
        var blob = new Mock<BlobClient>();
        blob.Setup(x => x.DownloadContentAsync(It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(
            Response.FromValue(BlobsModelFactory.BlobDownloadResult(BinaryData.FromString(Content),
                BlobsModelFactory.BlobDownloadDetails(eTag: new ETag($"\"{version}\""))), Mock.Of<Response>())));
        blob.Setup(x => x.UploadAsync(It.IsAny<BinaryData>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .Returns((BinaryData data, BlobUploadOptions options, CancellationToken _) =>
            {
                UploadAttempts++;
                LastIfMatch = options.Conditions.IfMatch!.Value;
                if (ConflictsRemaining > 0) { ConflictsRemaining--; version++; throw new RequestFailedException(412, "Simulated conflict"); }
                if (LastIfMatch != new ETag($"\"{version}\"")) throw new RequestFailedException(412, "Stale ETag");
                Content = data.ToString();
                SuccessfulWrites++;
                version++;
                return Task.FromResult(Response.FromValue(BlobsModelFactory.BlobContentInfo(new ETag($"\"{version}\""), DateTimeOffset.UtcNow, [], "", "", 0), Mock.Of<Response>()));
            });
        var container = new Mock<BlobContainerClient>();
        container.Setup(x => x.CreateIfNotExistsAsync(PublicAccessType.None, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Response<BlobContainerInfo>)null!);
        container.Setup(x => x.GetBlobClient("inventory.json")).Returns(blob.Object);
        var client = new Mock<BlobServiceClient>();
        client.Setup(x => x.GetBlobContainerClient("inventory")).Returns(container.Object);
        return new BlobInventoryService(client.Object, NullLogger<BlobInventoryService>.Instance);
    }
}
