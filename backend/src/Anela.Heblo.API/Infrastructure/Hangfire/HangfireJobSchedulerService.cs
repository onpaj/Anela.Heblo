using Hangfire;
using Microsoft.Extensions.Options;
using Anela.Heblo.API.Extensions;

namespace Anela.Heblo.API.Services;

public class HangfireJobSchedulerService : IHostedService
{
    private readonly ILogger<HangfireJobSchedulerService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly HangfireOptions _hangfireOptions;

    private const string QueueName = "heblo";

    public HangfireJobSchedulerService(
        ILogger<HangfireJobSchedulerService> logger,
        IWebHostEnvironment environment,
        IOptions<HangfireOptions> hangfireOptions)
    {
        _logger = logger;
        _environment = environment;
        _hangfireOptions = hangfireOptions.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Hangfire job scheduler service in {Environment} environment with SchedulerEnabled={SchedulerEnabled}",
            _environment.EnvironmentName, _hangfireOptions.SchedulerEnabled);

        // Check if scheduler is enabled via configuration
        if (!_hangfireOptions.SchedulerEnabled)
        {
            _logger.LogInformation("Hangfire job scheduler is disabled via configuration (SchedulerEnabled=false). No recurring jobs will be registered.");
            return Task.CompletedTask;
        }

        try
        {
            // Register recurring jobs

            // Purchase price recalculation daily at 2:00 AM UTC
            RecurringJob.AddOrUpdate<HangfireBackgroundJobService>(
                "purchase-price-recalculation",
                service => service.RecalculatePurchasePricesAsync(),
                "0 2 * * *", // Daily at 2:00 AM UTC
                timeZone: TimeZoneInfo.Utc,
                queue: QueueName
            );

            // Product export download daily at 2:00 AM UTC
            RecurringJob.AddOrUpdate<HangfireBackgroundJobService>(
                "product-export-download",
                service => service.DownloadProductExportAsync(),
                "0 2 * * *", // Daily at 2:00 AM UTC
                timeZone: TimeZoneInfo.Utc,
                queue: QueueName
            );

            // Product weight recalculation daily at 2:00 AM UTC
            RecurringJob.AddOrUpdate<HangfireBackgroundJobService>(
                "product-weight-recalculation",
                service => service.RecalculateProductWeightsAsync(),
                "0 2 * * *", // Daily at 2:00 AM UTC
                timeZone: TimeZoneInfo.Utc,
                queue: QueueName
            );

            _logger.LogInformation("Hangfire recurring jobs registered successfully in {Environment} environment", _environment.EnvironmentName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Hangfire recurring jobs in {Environment} environment. Application startup will continue, but background jobs will not be scheduled.", _environment.EnvironmentName);
            // Don't throw - let application start even if Hangfire job registration fails
            // This allows the application to be functional even with Hangfire issues
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Hangfire job scheduler service");
        return Task.CompletedTask;
    }
}