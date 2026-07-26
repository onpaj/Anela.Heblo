using System.Net;
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Features.FileStorage.Validators;
using Anela.Heblo.Domain.Features.FileStorage;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.FileStorage;

public static class FileStorageModule
{
    public const string FileDownloadClientName = FileStorageConstants.FileDownloadClientName;

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

        // Register named HttpClient for product export downloads.
        // PooledConnectionLifetime recycles sockets and refreshes DNS every 5 minutes,
        // preventing the stale-socket and DNS-pinning problems of a long-lived singleton HttpClient.
        // AutomaticDecompression handles gzip/brotli responses from the export URL transparently.
        services.AddHttpClient(FileDownloadClientName)
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

        services.Configure<FileDownloadOptions>(configuration.GetSection("FileStorage:Download"));

        // Register validator + pipeline behavior for DownloadFromUrlRequest, mirroring
        // AnalyticsModule's ValidationResultBehavior wiring (non-throwing, reconstructs
        // the response's own Success/ErrorCode/Params contract instead of throwing).
        services.AddScoped<IValidator<DownloadFromUrlRequest>, DownloadFromUrlRequestValidator>();
        services.AddScoped<IPipelineBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>,
            ValidationResultBehavior<DownloadFromUrlRequest, DownloadFromUrlResponse>>();

        return services;
    }
}
