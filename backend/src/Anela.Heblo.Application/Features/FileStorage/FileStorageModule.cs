using System.Net;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.Services;
using Anela.Heblo.Domain.Features.FileStorage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.FileStorage;

public static class FileStorageModule
{
    public const string ProductExportDownloadClientName = "ProductExportDownload";

    public static IServiceCollection AddFileStorageModule(this IServiceCollection services, IConfiguration configuration)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        // Register Azure Blob Storage client
        var connectionString = configuration["ExpeditionList:BlobConnectionString"];
        if (!string.IsNullOrEmpty(connectionString))
        {
            services.AddSingleton<BlobServiceClient>(provider => new BlobServiceClient(connectionString));
        }
        else
        {
            // For development, use Azure Storage Emulator
            services.AddSingleton<BlobServiceClient>(provider => new BlobServiceClient("UseDevelopmentStorage=true"));
        }

        // Register named HttpClient for product export downloads.
        // PooledConnectionLifetime recycles sockets and refreshes DNS every 5 minutes,
        // preventing the stale-socket and DNS-pinning problems of a long-lived singleton HttpClient.
        // AutomaticDecompression handles gzip/brotli responses from the export URL transparently.
        services.AddHttpClient(ProductExportDownloadClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All,
            })
            .ConfigureHttpClient(c =>
            {
                // Intentional: per-call timeout is enforced by linked CancellationTokenSource
                // inside DownloadResilienceService and around the HEAD probe in
                // DownloadFromUrlHandler. HttpClient.Timeout is left infinite so it does
                // not race with the linked CTS.
                c.Timeout = Timeout.InfiniteTimeSpan;
            });

        // Register resilience service as Singleton — it holds no request state and
        // its internal Polly pipeline is rebuilt per-call (see BuildPipeline).
        services.AddSingleton<IDownloadResilienceService, DownloadResilienceService>();

        // Register blob storage service as Singleton so the _containerExists cache survives across requests.
        // BlobServiceClient is already Singleton — no thread-safety concerns.
        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

        return services;
    }
}
