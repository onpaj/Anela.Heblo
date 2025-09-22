using Hangfire;
using Microsoft.CodeAnalysis.Options;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

public class HangfireJobSchedulerService : IHostedService
{
    private readonly ILogger<HangfireJobSchedulerService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IOptions<HangfireOptions> _options;

    public HangfireJobSchedulerService(ILogger<HangfireJobSchedulerService> logger, IWebHostEnvironment environment, IOptions<HangfireOptions> options)
    {
        _logger = logger;
        _environment = environment;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Hangfire job scheduler service in {Environment} environment", _environment.EnvironmentName);

        // Additional safety check - only register jobs in Production and Staging
        if (!_options.Value.SchedulerEnabled)
        {
            _logger.LogWarning("Hangfire job scheduler service is running in {Environment} environment. Jobs will NOT be scheduled. Only Production and Staging environments schedule background jobs.", _environment.EnvironmentName);
            return Task.CompletedTask;
        }

        try
        {
            // Register recurring jobs - in Production and Staging

            // Purchase price recalculation daily at 2:00 AM UTC
            RecurringJob.AddOrUpdate<HangfireBackgroundJobService>(
                "purchase-price-recalculation",
                service => service.RecalculatePurchasePricesAsync(),
                "0 2 * * *", // Daily at 2:00 AM UTC
                timeZone: TimeZoneInfo.Utc,
                queue: _options.Value.QueueName
            );

            // Product export download daily at 2:00 AM UTC
            RecurringJob.AddOrUpdate<HangfireBackgroundJobService>(
                "product-export-download",
                service => service.DownloadProductExportAsync(),
                "0 2 * * *", // Daily at 2:00 AM UTC
                timeZone: TimeZoneInfo.Utc,
                queue: _options.Value.QueueName
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