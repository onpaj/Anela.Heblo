using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;

public class TriggerRecurringJobHandler : IRequestHandler<TriggerRecurringJobRequest, TriggerRecurringJobResponse>
{
    private readonly IEnumerable<IRecurringJob> _jobs;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<TriggerRecurringJobHandler> _logger;

    public TriggerRecurringJobHandler(
        IEnumerable<IRecurringJob> jobs,
        IRecurringJobStatusChecker statusChecker,
        ILogger<TriggerRecurringJobHandler> logger)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TriggerRecurringJobResponse> Handle(TriggerRecurringJobRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to trigger job {JobName} (forceDisabled={ForceDisabled})",
            request.JobName, request.ForceDisabled);

        // Find the job instance
        var job = _jobs.FirstOrDefault(j => j.Metadata.JobName == request.JobName);

        if (job == null)
        {
            _logger.LogWarning("Job {JobName} not found in registered jobs", request.JobName);
            return new TriggerRecurringJobResponse(
                ErrorCodes.RecurringJobNotFound,
                new Dictionary<string, string>
                {
                    { "jobName", request.JobName },
                    { "forceDisabled", request.ForceDisabled.ToString() }
                }
            );
        }

        // Check if job is enabled (unless forced)
        if (!request.ForceDisabled)
        {
            var isEnabled = await _statusChecker.IsJobEnabledAsync(request.JobName, cancellationToken);
            if (!isEnabled)
            {
                _logger.LogWarning("Job {JobName} is disabled. Use forceDisabled=true to trigger anyway.", request.JobName);
                return new TriggerRecurringJobResponse(
                    ErrorCodes.RecurringJobNotFound,
                    new Dictionary<string, string>
                    {
                        { "jobName", request.JobName },
                        { "forceDisabled", request.ForceDisabled.ToString() }
                    }
                );
            }
        }

        // Enqueue the job for immediate execution via Hangfire
        var jobType = job.GetType();
        var executeMethod = typeof(IRecurringJob).GetMethod(nameof(IRecurringJob.ExecuteAsync));

        if (executeMethod == null)
        {
            _logger.LogError("Could not find ExecuteAsync method on IRecurringJob");
            return new TriggerRecurringJobResponse(
                ErrorCodes.RecurringJobNotFound,
                new Dictionary<string, string>
                {
                    { "jobName", request.JobName },
                    { "forceDisabled", request.ForceDisabled.ToString() }
                }
            );
        }

        // Use BackgroundJob static class to enqueue
        // Find the generic Enqueue<T> method that takes Expression<Func<T, Task>>
        // IMPORTANT: We need the async overload, not the sync Action<T> overload
        var enqueueMethod = typeof(BackgroundJob)
            .GetMethods()
            .Where(m => m.Name == "Enqueue" && m.IsGenericMethodDefinition)
            .FirstOrDefault(m =>
            {
                var parameters = m.GetParameters();
                if (parameters.Length != 1) return false;

                var paramType = parameters[0].ParameterType;
                if (!paramType.IsGenericType) return false;

                // Check if parameter is Expression<...>
                var genericTypeDef = paramType.GetGenericTypeDefinition();
                if (genericTypeDef != typeof(System.Linq.Expressions.Expression<>)) return false;

                // Get the inner type (should be Func<T, Task> or Action<T>)
                var innerType = paramType.GetGenericArguments()[0];
                if (!innerType.IsGenericType) return false;

                // We want Func<T, Task>, not Action<T>
                var innerGenericDef = innerType.GetGenericTypeDefinition();
                if (innerGenericDef != typeof(Func<,>)) return false;

                // Verify the Func has 2 generic arguments (T and TResult)
                var funcArgs = innerType.GetGenericArguments();
                if (funcArgs.Length != 2) return false;

                // Second argument should be Task
                return funcArgs[1] == typeof(Task);
            });

        if (enqueueMethod == null)
        {
            _logger.LogError("Could not find suitable Enqueue method on BackgroundJob static class");
            return new TriggerRecurringJobResponse(
                ErrorCodes.RecurringJobNotFound,
                new Dictionary<string, string>
                {
                    { "jobName", request.JobName },
                    { "forceDisabled", request.ForceDisabled.ToString() }
                }
            );
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

        _logger.LogInformation("Job {JobName} enqueued with Hangfire job ID: {JobId}", request.JobName, jobId);

        return new TriggerRecurringJobResponse
        {
            JobId = jobId!
        };
    }
}
