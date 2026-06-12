using System.Net;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.Services;
using Anela.Heblo.Domain.Features.FileStorage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.FileStorage;

public static class FileStorageModule
{
    public const string ProductExportDownloadClientName = "ProductExportDownload";

    public static IServiceCollection AddFileStorageModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        var optionsBuilder = services
            .AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName));

        if (!environment.IsDevelopment())
        {
            // Fail fast in non-Development environments: missing or whitespace connection string
            // surfaces at startup, never silently as a write to the storage emulator in production.
            optionsBuilder
                .Validate(
                    o => !string.IsNullOrWhiteSpace(o.BlobConnectionString),
                    $"{FileStorageOptions.SectionName}:{nameof(FileStorageOptions.BlobConnectionString)} must be configured.")
                .ValidateOnStart();
        }

        // Register Azure Blob Storage client. The factory reads the already-validated options,
        // so ValidateOnStart() runs before any consumer resolves the BlobServiceClient.
        services.AddSingleton<BlobServiceClient>(provider =>
        {
            var opts = provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;
            if (string.IsNullOrWhiteSpace(opts.BlobConnectionString))
            {
                // Reachable only in Development — validation blocks the empty path elsewhere.
                // Log a warning so the storage-emulator fallback is never silent.
                var logger = provider.GetRequiredService<ILogger<AzureBlobStorageService>>();
                logger.LogWarning(
                    "FileStorage:BlobConnectionString is empty in {Environment}; falling back to UseDevelopmentStorage=true.",
                    environment.EnvironmentName);
                return new BlobServiceClient("UseDevelopmentStorage=true");
            }

            return new BlobServiceClient(opts.BlobConnectionString);
        });

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
