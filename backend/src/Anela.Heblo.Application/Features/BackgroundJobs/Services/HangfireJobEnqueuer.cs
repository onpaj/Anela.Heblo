using System.Linq.Expressions;
using System.Reflection;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Service responsible for enqueueing recurring jobs via Hangfire using reflection.
/// Uses reflection to dynamically call BackgroundJob.Enqueue with proper generic type resolution.
/// </summary>
public class HangfireJobEnqueuer : IHangfireJobEnqueuer
{
    private readonly ILogger<HangfireJobEnqueuer> _logger;

    public HangfireJobEnqueuer(ILogger<HangfireJobEnqueuer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Enqueues a recurring job for immediate execution using Hangfire's BackgroundJob.Enqueue.
    /// </summary>
    /// <param name="job">The recurring job instance to enqueue</param>
    /// <param name="cancellationToken">Cancellation token to pass to the job execution</param>
    /// <returns>Hangfire job ID if successful, null if enqueue failed</returns>
    public string? EnqueueJob(IRecurringJob job, CancellationToken cancellationToken)
    {
        if (job == null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        var jobType = job.GetType();
        var executeMethod = typeof(IRecurringJob).GetMethod(nameof(IRecurringJob.ExecuteAsync));

        if (executeMethod == null)
        {
            _logger.LogError("Could not find ExecuteAsync method on IRecurringJob");
            return null;
        }

        // Find the generic Enqueue<T> method that takes Expression<Func<T, Task>>
        var enqueueMethod = FindEnqueueMethod();

        if (enqueueMethod == null)
        {
            _logger.LogError("Could not find suitable Enqueue method on BackgroundJob static class");
            return null;
        }

        var genericMethod = enqueueMethod.MakeGenericMethod(jobType);

        // Create lambda: (TJob job) => job.ExecuteAsync(cancellationToken)
        var lambda = CreateExecutionExpression(jobType, executeMethod, cancellationToken);

        // Enqueue the job using reflection on BackgroundJob.Enqueue<T>
        var jobId = (string?)genericMethod.Invoke(null, new object[] { lambda });

        _logger.LogInformation("Job {JobType} enqueued with Hangfire job ID: {JobId}", jobType.Name, jobId);

        return jobId;
    }

    /// <summary>
    /// Finds the Enqueue method on the BackgroundJob static class that accepts Expression&lt;Func&lt;T, Task&gt;&gt;.
    /// </summary>
    /// <returns>MethodInfo for the generic Enqueue method, or null if not found</returns>
    private MethodInfo? FindEnqueueMethod()
    {
        return typeof(BackgroundJob)
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
                if (genericTypeDef != typeof(Expression<>)) return false;

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
    }

    /// <summary>
    /// Creates an expression tree representing the job execution:
    /// (TJob job) => job.ExecuteAsync(cancellationToken)
    /// </summary>
    /// <param name="jobType">The concrete job type</param>
    /// <param name="executeMethod">The ExecuteAsync method to call</param>
    /// <param name="cancellationToken">The cancellation token to pass to the method</param>
    /// <returns>Lambda expression for job execution</returns>
    private LambdaExpression CreateExecutionExpression(
        Type jobType,
        MethodInfo executeMethod,
        CancellationToken cancellationToken)
    {
        var parameter = Expression.Parameter(jobType, "job");
        var methodCall = Expression.Call(
            parameter,
            executeMethod,
            Expression.Constant(cancellationToken, typeof(CancellationToken))
        );
        var lambda = Expression.Lambda(methodCall, parameter);

        return lambda;
    }
}
