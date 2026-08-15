using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tacc.Api.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var inventoryStorageConnection = context.Configuration["InventoryStorageConnection"];
        if (string.IsNullOrWhiteSpace(inventoryStorageConnection))
        {
            throw new InvalidOperationException(
                "The InventoryStorageConnection configuration setting is required.");
        }

        services.AddSingleton(new BlobServiceClient(inventoryStorageConnection));
        services.AddSingleton<IInventoryService, BlobInventoryService>();
    })
    .Build();

host.Run();
