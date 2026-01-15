using Anela.Heblo.API.Extensions;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

/// <summary>
/// Automatically discovers and registers all IRecurringJob implementations with Hangfire
/// </summary>
public class RecurringJobDiscoveryService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecurringJobDiscoveryService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly HangfireOptions _hangfireOptions;

    public RecurringJobDiscoveryService(
        IServiceProvider serviceProvider,
        ILogger<RecurringJobDiscoveryService> logger,
        IWebHostEnvironment environment,
        IOptions<HangfireOptions> hangfireOptions)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _environment = environment;
        _hangfireOptions = hangfireOptions.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting recurring job discovery in {Environment} with SchedulerEnabled={SchedulerEnabled}",
            _environment.EnvironmentName, _hangfireOptions.SchedulerEnabled);

        if (!_hangfireOptions.SchedulerEnabled)
        {
            _logger.LogInformation("Hangfire scheduler disabled via configuration (SchedulerEnabled=false). No recurring jobs will be registered.");
            return Task.CompletedTask;
        }

        try
        {
            // Discover all IRecurringJob implementations via DI
            using var scope = _serviceProvider.CreateScope();
            var jobs = scope.ServiceProvider.GetServices<IRecurringJob>();

            var jobList = jobs.ToList();
            if (jobList.Count == 0)
            {
                _logger.LogWarning("No IRecurringJob implementations found. Ensure jobs are registered in DI container.");
                return Task.CompletedTask;
            }

            foreach (var job in jobList)
            {
                var metadata = job.Metadata;
                var jobType = job.GetType();

                try
                {
                    // Call the generic helper method using reflection
                    var registerMethod = typeof(RecurringJobDiscoveryService)
                        .GetMethod(nameof(RegisterRecurringJobInternal), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                    if (registerMethod == null)
                    {
                        _logger.LogError("Could not find RegisterRecurringJobInternal method");
                        continue;
                    }

                    var genericRegisterMethod = registerMethod.MakeGenericMethod(jobType);

                    // Invoke the helper method with properly typed parameters
                    genericRegisterMethod.Invoke(null, new object[]
                    {
                        metadata.JobName,
                        metadata.CronExpression,
                        metadata.TimeZoneId,
                        metadata.QueueName
                    });

                    _logger.LogInformation("Registered recurring job: {JobName} ({JobType}) with schedule {Cron} in queue {Queue}",
                        metadata.JobName, jobType.Name, metadata.CronExpression, metadata.QueueName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to register recurring job {JobName} ({JobType})",
                        metadata.JobName, jobType.Name);
                }
            }

            _logger.LogInformation("Successfully registered {Count} recurring jobs in {Environment} environment",
                jobList.Count, _environment.EnvironmentName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register recurring jobs in {Environment} environment. Application startup will continue, but background jobs will not be scheduled.",
                _environment.EnvironmentName);
            // Don't throw - let application start even if Hangfire job registration fails
            // This allows the application to be functional even with Hangfire issues
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping recurring job discovery service");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Generic helper method that calls Hangfire's AddOrUpdate with proper static typing.
    /// This avoids complex reflection to find the correct overload method.
    /// </summary>
    private static void RegisterRecurringJobInternal<TJob>(
        string jobName,
        string cronExpression,
        string timeZoneId,
        string queueName) where TJob : IRecurringJob
    {
        RecurringJob.AddOrUpdate<TJob>(
            jobName,
            job => job.ExecuteAsync(default),
            cronExpression,
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId),
            queueName);
    }
}
