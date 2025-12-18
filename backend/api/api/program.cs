using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Cosmos;

// var host = new HostBuilder()
//     .ConfigureFunctionsWebApplication()
//     .ConfigureServices(services => {
//         services.AddApplicationInsightsTelemetryWorkerService();
//         services.ConfigureFunctionsApplicationInsights();
//     })
//     .Build();

// host.Run();

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;


var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Add CosmosClient for throttling logic (SDK usage)
        var conn = Environment.GetEnvironmentVariable("AzureResumeConnectionString");
        services.AddSingleton(_ => new CosmosClient(conn));
    })
    .Build();

host.Run();
