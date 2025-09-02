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

        // Additional safety check - only register jobs in Production and Staging
        if (!_environment.IsProduction() && !_environment.IsStaging())
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
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Utc
                });

            _logger.LogInformation("Hangfire recurring jobs registered successfully in {Environment} environment", _environment.EnvironmentName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Hangfire recurring jobs in {Environment} environment", _environment.EnvironmentName);
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