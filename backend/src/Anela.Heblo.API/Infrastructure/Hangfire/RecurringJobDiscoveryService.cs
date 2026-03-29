using Anela.Heblo.API.Extensions;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

/// <summary>
/// Automatically discovers and registers all IRecurringJob implementations with Hangfire.
/// Uses DB-stored CRON expressions (seeded from metadata on first run) so runtime
/// changes survive application restarts.
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting recurring job discovery in {Environment} with SchedulerEnabled={SchedulerEnabled}",
            _environment.EnvironmentName, _hangfireOptions.SchedulerEnabled);

        if (!_hangfireOptions.SchedulerEnabled)
        {
            _logger.LogInformation(
                "Hangfire scheduler disabled via configuration (SchedulerEnabled=false). No recurring jobs will be registered.");
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var jobs = scope.ServiceProvider.GetServices<IRecurringJob>().ToList();

            if (jobs.Count == 0)
            {
                _logger.LogWarning(
                    "No IRecurringJob implementations found. Ensure jobs are registered in DI container.");
                return;
            }

            // Load all DB configs once — seeding guarantees records exist for all registered jobs
            var repository = scope.ServiceProvider.GetRequiredService<IRecurringJobConfigurationRepository>();
            var dbConfigs = await repository.GetAllAsync(cancellationToken);
            var configByJobName = dbConfigs.ToDictionary(c => c.JobName, c => c);

            foreach (var job in jobs)
            {
                var metadata = job.Metadata;
                var jobType = job.GetType();

                try
                {
                    // Prefer DB CRON (runtime override); fall back to metadata default
                    var cronSource = "metadata";
                    var cronExpression = metadata.CronExpression;

                    if (configByJobName.TryGetValue(metadata.JobName, out var dbConfig))
                    {
                        cronExpression = dbConfig.CronExpression;
                        cronSource = "DB";
                    }

                    var registerMethod = typeof(RecurringJobDiscoveryService)
                        .GetMethod(
                            nameof(RegisterRecurringJobInternal),
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                    if (registerMethod == null)
                    {
                        _logger.LogError("Could not find RegisterRecurringJobInternal method");
                        continue;
                    }

                    var genericRegisterMethod = registerMethod.MakeGenericMethod(jobType);
                    genericRegisterMethod.Invoke(null, new object[]
                    {
                        metadata.JobName,
                        cronExpression,
                        metadata.TimeZoneId
                    });

                    _logger.LogInformation(
                        "Registered recurring job: {JobName} ({JobType}) with schedule {Cron} (from {CronSource})",
                        metadata.JobName, jobType.Name, cronExpression, cronSource);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to register recurring job {JobName} ({JobType})",
                        metadata.JobName, jobType.Name);
                }
            }

            _logger.LogInformation(
                "Successfully registered {Count} recurring jobs in {Environment} environment",
                jobs.Count, _environment.EnvironmentName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to register recurring jobs in {Environment} environment. " +
                "Application startup will continue, but background jobs will not be scheduled.",
                _environment.EnvironmentName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping recurring job discovery service");
        return Task.CompletedTask;
    }

    private static void RegisterRecurringJobInternal<TJob>(
        string jobName,
        string cronExpression,
        string timeZoneId) where TJob : IRecurringJob
    {
        RecurringJob.AddOrUpdate<TJob>(
            jobName,
            job => job.ExecuteAsync(default),
            cronExpression,
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
    }
}
