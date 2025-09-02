using Hangfire;

namespace Anela.Heblo.API.Services;

public class HangfireJobSchedulerService : IHostedService
{
    private readonly ILogger<HangfireJobSchedulerService> _logger;

    public HangfireJobSchedulerService(ILogger<HangfireJobSchedulerService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Hangfire job scheduler service");

        try
        {
            // Register recurring jobs
            
            // Purchase price recalculation daily at 2:00 AM UTC
            RecurringJob.AddOrUpdate<HangfireBackgroundJobService>(
                "purchase-price-recalculation",
                service => service.RecalculatePurchasePricesAsync(),
                "0 2 * * *", // Daily at 2:00 AM UTC
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Utc
                });

            _logger.LogInformation("Hangfire recurring jobs registered successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Hangfire recurring jobs");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Hangfire job scheduler service");
        return Task.CompletedTask;
    }
}