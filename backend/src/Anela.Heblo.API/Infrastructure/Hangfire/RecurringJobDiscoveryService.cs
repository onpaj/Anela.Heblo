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

                // Build expression using reflection to call ExecuteAsync method
                var executeMethod = typeof(IRecurringJob).GetMethod(nameof(IRecurringJob.ExecuteAsync));
                if (executeMethod == null)
                {
                    _logger.LogError("Could not find ExecuteAsync method on {JobType}", jobType.Name);
                    continue;
                }

                // Use Hangfire's AddOrUpdate<T> method with reflection
                var addOrUpdateMethod = typeof(RecurringJob)
                    .GetMethods()
                    .Where(m => m.Name == "AddOrUpdate" && m.IsGenericMethodDefinition)
                    .FirstOrDefault(m =>
                    {
                        var parameters = m.GetParameters();
                        return parameters.Length == 5 &&
                               parameters[0].ParameterType == typeof(string) &&
                               parameters[4].ParameterType == typeof(string);
                    });

                if (addOrUpdateMethod == null)
                {
                    _logger.LogError("Could not find suitable AddOrUpdate method");
                    continue;
                }

                var genericMethod = addOrUpdateMethod.MakeGenericMethod(jobType);

                // Create the lambda expression: job => job.ExecuteAsync(default)
                var parameter = System.Linq.Expressions.Expression.Parameter(jobType, "job");
                var methodCall = System.Linq.Expressions.Expression.Call(
                    parameter,
                    executeMethod,
                    System.Linq.Expressions.Expression.Default(typeof(CancellationToken))
                );
                var lambda = System.Linq.Expressions.Expression.Lambda(methodCall, parameter);

                // Invoke AddOrUpdate<TJob>(string, Expression<Action<TJob>>, string, TimeZoneInfo, string)
                genericMethod.Invoke(null, new object[]
                {
                    metadata.JobName,
                    lambda,
                    metadata.CronExpression,
                    TimeZoneInfo.FindSystemTimeZoneById(metadata.TimeZoneId),
                    metadata.QueueName
                });

                _logger.LogInformation("Registered recurring job: {JobName} ({JobType}) with schedule {Cron} in queue {Queue}",
                    metadata.JobName, jobType.Name, metadata.CronExpression, metadata.QueueName);
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
}
