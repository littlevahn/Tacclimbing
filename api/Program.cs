using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Future application services can be registered here and injected into functions.
        services.AddOptions();
    })
    .Build();

host.Run();
