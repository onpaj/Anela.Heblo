using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

public class RecurringJobTriggerService : IRecurringJobTriggerService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<RecurringJobTriggerService> _logger;

    public RecurringJobTriggerService(
        IServiceProvider serviceProvider,
        IRecurringJobStatusChecker statusChecker,
        ILogger<RecurringJobTriggerService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> TriggerJobAsync(string jobName, bool forceDisabled = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to trigger job {JobName} (forceDisabled={ForceDisabled})",
            jobName, forceDisabled);

        // Find the job instance from DI
        using var scope = _serviceProvider.CreateScope();
        var jobs = scope.ServiceProvider.GetServices<IRecurringJob>();
        var job = jobs.FirstOrDefault(j => j.Metadata.JobName == jobName);

        if (job == null)
        {
            _logger.LogWarning("Job {JobName} not found in registered jobs", jobName);
            return null;
        }

        // Check if job is enabled (unless forced)
        if (!forceDisabled)
        {
            var isEnabled = await _statusChecker.IsJobEnabledAsync(jobName, cancellationToken);
            if (!isEnabled)
            {
                _logger.LogWarning("Job {JobName} is disabled. Use forceDisabled=true to trigger anyway.", jobName);
                return null;
            }
        }

        // Enqueue the job for immediate execution via Hangfire
        var jobType = job.GetType();
        var executeMethod = typeof(IRecurringJob).GetMethod(nameof(IRecurringJob.ExecuteAsync));

        if (executeMethod == null)
        {
            _logger.LogError("Could not find ExecuteAsync method on IRecurringJob");
            return null;
        }

        // Use BackgroundJob static class to enqueue
        // Find the generic Enqueue<T> method that takes Expression<Func<T, Task>>
        var enqueueMethod = typeof(BackgroundJob)
            .GetMethods()
            .Where(m => m.Name == "Enqueue" && m.IsGenericMethodDefinition)
            .FirstOrDefault(m =>
            {
                var parameters = m.GetParameters();
                if (parameters.Length != 1) return false;

                var paramType = parameters[0].ParameterType;
                if (!paramType.IsGenericType) return false;

                var genericTypeDef = paramType.GetGenericTypeDefinition();
                return genericTypeDef == typeof(System.Linq.Expressions.Expression<>);
            });

        if (enqueueMethod == null)
        {
            _logger.LogError("Could not find suitable Enqueue method on BackgroundJob static class");
            return null;
        }

        var genericMethod = enqueueMethod.MakeGenericMethod(jobType);

        // Create lambda: (TJob job) => job.ExecuteAsync(default(CancellationToken))
        var parameter = System.Linq.Expressions.Expression.Parameter(jobType, "job");
        var methodCall = System.Linq.Expressions.Expression.Call(
            parameter,
            executeMethod,
            System.Linq.Expressions.Expression.Default(typeof(CancellationToken))
        );
        var lambda = System.Linq.Expressions.Expression.Lambda(methodCall, parameter);

        // Enqueue the job using reflection on BackgroundJob.Enqueue<T>
        var jobId = (string?)genericMethod.Invoke(null, new object[] { lambda });

        _logger.LogInformation("Job {JobName} enqueued with Hangfire job ID: {JobId}", jobName, jobId);

        return jobId;
    }
}
