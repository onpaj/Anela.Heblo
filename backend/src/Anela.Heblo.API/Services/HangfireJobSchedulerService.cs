using Hangfire;

namespace Anela.Heblo.API.Services;

public class HangfireJobSchedulerService : IHostedService
{
    private readonly ILogger<HangfireJobSchedulerService> _logger;
    private readonly IWebHostEnvironment _environment;

    public HangfireJobSchedulerService(ILogger<HangfireJobSchedulerService> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Hangfire job scheduler service in {Environment} environment", _environment.EnvironmentName);

        // Additional safety check - only register jobs in Production
        if (!_environment.IsProduction())
        {
            _logger.LogWarning("Hangfire job scheduler service is running in {Environment} environment. Jobs will NOT be scheduled. Only Production environment schedules background jobs.", _environment.EnvironmentName);
            return Task.CompletedTask;
        }

        try
        {
            // Register recurring jobs - only in Production

            // Purchase price recalculation daily at 2:00 AM UTC
            RecurringJob.AddOrUpdate<HangfireBackgroundJobService>(
                "purchase-price-recalculation",
                service => service.RecalculatePurchasePricesAsync(),
                "0 2 * * *", // Daily at 2:00 AM UTC
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Utc
                });

            _logger.LogInformation("Hangfire recurring jobs registered successfully in Production environment");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Hangfire recurring jobs in Production environment");
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