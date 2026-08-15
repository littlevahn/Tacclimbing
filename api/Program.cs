using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tacc.Api.Authentication;
using Tacc.Api.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(workerApplication =>
    {
        workerApplication.UseMiddleware<AdminAuthenticationMiddleware>();
    })
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
        services.Configure<EntraAuthenticationOptions>(
            context.Configuration.GetSection(EntraAuthenticationOptions.SectionName));
        services.AddSingleton<IEntraTokenValidator, EntraTokenValidator>();
        services.AddSingleton<IAuthorizationHandler, InventoryAdminAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                InventoryAdminAuthorization.PolicyName,
                policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new InventoryAdminAuthorizationRequirement()));
        });
    })
    .Build();

host.Run();
