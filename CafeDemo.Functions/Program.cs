using System;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
        services.AddSingleton(_ => new BlobServiceClient(connectionString));
        services.AddSingleton(_ => new TableServiceClient(connectionString));
    })
    .ConfigureLogging(logging => logging.AddConsole())
    .Build();

host.Run();
