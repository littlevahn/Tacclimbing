using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tacc.Api.Authentication;
using Tacc.Api.Services;
using Tacc.Api.Stripe;

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
        services.Configure<StripeOptions>(context.Configuration.GetSection(StripeOptions.SectionName));
        services.Configure<AdminAuthenticationOptions>(options =>
        {
            options.Username = context.Configuration["ADMIN_USERNAME"];
            options.PasswordHash = context.Configuration["ADMIN_PASSWORD_HASH"];
            options.SessionSecret = context.Configuration["ADMIN_SESSION_SECRET"];
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AdminAuthenticationService>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton(new LoginSourceResolver(
            !string.IsNullOrWhiteSpace(context.Configuration["WEBSITE_INSTANCE_ID"])));
    })
    .Build();

// Validate admin settings at initialization without taking public/Stripe endpoints offline.
_ = host.Services.GetRequiredService<AdminAuthenticationService>();
host.Run();
