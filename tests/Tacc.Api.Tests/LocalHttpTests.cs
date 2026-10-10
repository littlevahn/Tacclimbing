using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Xunit.Abstractions;

namespace Tacc.Api.Tests;

// Opt in with TACC_FUNCTIONS_HOST and TACC_AZURITE pointing to local executables.
// Uses a disposable copy of Release output and a separate storage emulator/account.
public sealed class LocalHttpTests(ITestOutputHelper output)
{
    [LocalHttpFact]
    public async Task RegisteredRoutesWorkThroughTheRealFunctionsHost()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Tacc.sln"))) repo = repo.Parent;
        Assert.NotNull(repo);
        var source = Path.Combine(repo.FullName, "api", "bin", "Release", "net9.0");
        Assert.True(File.Exists(Path.Combine(source, "functions.metadata")), "Build the API in Release before running HTTP tests.");
        var workspace = Path.Combine(repo.FullName, "tests", "Tacc.Api.Tests", "obj", "http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        Process? emulator = null;
        Process? host = null;
        try
        {
            var app = Path.Combine(workspace, "app");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "local.settings.json") continue;
                var target = Path.Combine(app, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            await File.WriteAllTextAsync(Path.Combine(app, "local.settings.json"),
                """{"IsEncrypted":false,"Values":{"FUNCTIONS_WORKER_RUNTIME":"dotnet-isolated"}}""");
            var blobPort = FreePort();
            var queuePort = FreePort();
            var tablePort = FreePort();
            var httpPort = FreePort();
            var storageKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var storage = $"DefaultEndpointsProtocol=http;AccountName=localtest;AccountKey={storageKey};BlobEndpoint=http://127.0.0.1:{blobPort}/localtest;QueueEndpoint=http://127.0.0.1:{queuePort}/localtest;TableEndpoint=http://127.0.0.1:{tablePort}/localtest;";
            var emulatorInfo = StartInfo(Environment.GetEnvironmentVariable("TACC_AZURITE")!, workspace);
            foreach (var arg in new[] { "--silent", "--disableTelemetry", "--skipApiVersionCheck", "--location", Path.Combine(workspace, "storage"),
                "--blobPort", blobPort.ToString(), "--queuePort", queuePort.ToString(), "--tablePort", tablePort.ToString() }) emulatorInfo.ArgumentList.Add(arg);
            emulatorInfo.Environment["AZURITE_ACCOUNTS"] = "localtest:" + storageKey;
            emulator = Start(emulatorInfo);
            await WaitForPort(emulator, blobPort);
            var container = new BlobServiceClient(storage).GetBlobContainerClient("inventory");
            await container.CreateIfNotExistsAsync();
            await container.GetBlobClient("inventory.json").UploadAsync(BinaryData.FromString(
                """{"products":[{"key":"http-test","name":"HTTP test product","size":"M","quantity":3}]}"""));

            var hostInfo = StartInfo(Environment.GetEnvironmentVariable("TACC_FUNCTIONS_HOST")!, app);
            foreach (var arg in new[] { "start", "--no-build", "--port", httpPort.ToString() }) hostInfo.ArgumentList.Add(arg);
            hostInfo.Environment["FUNCTIONS_WORKER_RUNTIME"] = "dotnet-isolated";
            hostInfo.Environment["AzureWebJobsStorage"] = storage;
            hostInfo.Environment["InventoryStorageConnection"] = storage;
            hostInfo.Environment["ADMIN_USERNAME"] = TestAuthentication.Username;
            hostInfo.Environment["ADMIN_PASSWORD_HASH"] = TestAuthentication.Hash;
            hostInfo.Environment["ADMIN_SESSION_SECRET"] = Convert.ToBase64String(TestAuthentication.Key);
            hostInfo.Environment.Remove("WEBSITE_INSTANCE_ID");
            hostInfo.Environment["AzureWebJobsSecretStorageType"] = "files";
            hostInfo.Environment["FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT"] = "1";
            host = Start(hostInfo);
            await WaitForPort(host, httpPort);
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{httpPort}/api/"), Timeout = TimeSpan.FromSeconds(20) };
            using (var health = await client.GetAsync("health")) Check("GET /api/health", HttpStatusCode.OK, health);
            // Buffered JSON sends Content-Length, matching browser fetch requests.
            using (var empty = await client.PostAsync("admin/login", JsonBody(new { }))) Check("POST /api/admin/login empty JSON", HttpStatusCode.BadRequest, empty);
            using (var wrong = await client.PostAsync("admin/login", JsonBody(new { username = TestAuthentication.Username, password = "incorrect" })))
                Check("POST /api/admin/login incorrect credentials", HttpStatusCode.Unauthorized, wrong);
            using var valid = await client.PostAsync("admin/login", JsonBody(new { username = TestAuthentication.Username, password = TestAuthentication.Password }));
            Check("POST /api/admin/login valid credentials", HttpStatusCode.OK, valid);
            using var session = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
            var token = session.RootElement.GetProperty("accessToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(token));
            Assert.True(TestAuthentication.Create(new TestClock { Now = DateTimeOffset.UtcNow }).ValidateBearer("Bearer " + token)?.Identity?.IsAuthenticated);
            output.WriteLine("Access token received and signature/claims validated; token omitted.");
            using (var inventory = await client.GetAsync("inventory/http-test")) Check("GET /api/inventory/http-test", HttpStatusCode.OK, inventory);
            using (var deniedGet = await client.GetAsync("admin/inventory/http-test")) Check("GET /api/admin/inventory/http-test without authentication", HttpStatusCode.Unauthorized, deniedGet);
            using (var deniedPut = await client.PutAsync("admin/inventory/http-test", JsonBody(new { }))) Check("PUT /api/admin/inventory/http-test without authentication", HttpStatusCode.Unauthorized, deniedPut);
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            using (var allowed = await client.GetAsync("admin/inventory/http-test")) Check("GET /api/admin/inventory/http-test with token", HttpStatusCode.OK, allowed);
        }
        finally
        {
            Stop(host);
            Stop(emulator);
            // Verify the resolved deletion target stays in this project's ignored obj directory.
            var cleanupRoot = Path.GetFullPath(Path.Combine(repo.FullName, "tests", "Tacc.Api.Tests", "obj")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(workspace).StartsWith(cleanupRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The HTTP test cleanup path is outside its workspace.");
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static StringContent JsonBody(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private void Check(string request, HttpStatusCode expected, HttpResponseMessage response)
    {
        output.WriteLine($"{request}: {(int)response.StatusCode}");
        Assert.Equal(expected, response.StatusCode);
    }

    private static ProcessStartInfo StartInfo(string executable, string directory) => new(executable)
    {
        WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true
    };

    private Process Start(ProcessStartInfo info)
    {
        var process = new Process { StartInfo = info };
        // Keep startup evidence without dumping settings, payloads, or tokens.
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data?.Contains("conflicts with one or more built in routes", StringComparison.Ordinal) == true)
                output.WriteLine(e.Data);
            if (e.Data?.TrimStart().StartsWith("AdminLogin: [POST]", StringComparison.Ordinal) == true)
                output.WriteLine(e.Data.Trim());
        };
        process.ErrorDataReceived += (_, _) => { };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitForPort(Process process, int port)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Assert.False(process.HasExited, "Local test dependency exited before becoming ready.");
            using var socket = new TcpClient();
            try { await socket.ConnectAsync(IPAddress.Loopback, port); return; }
            catch (SocketException) { await Task.Delay(100); }
        }
        Assert.Fail("Local test dependency did not become ready within 20 seconds.");
    }

    private static void Stop(Process? process)
    {
        if (process is null) return;
        if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
        process.Dispose();
    }
}

internal sealed class LocalHttpFactAttribute : FactAttribute
{
    public LocalHttpFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TACC_FUNCTIONS_HOST")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TACC_AZURITE")))
            Skip = "Set TACC_FUNCTIONS_HOST and TACC_AZURITE to run real local HTTP verification.";
    }
}
