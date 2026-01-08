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

        // Find our internal generic wrapper method (provides design-time validation of Hangfire API)
        var enqueueWrapperMethod = typeof(HangfireJobEnqueuer)
            .GetMethod(nameof(EnqueueJobInternal), BindingFlags.NonPublic | BindingFlags.Static);

        if (enqueueWrapperMethod == null)
        {
            _logger.LogError("Could not find EnqueueJobInternal method");
            return null;
        }

        // Make it generic for the specific job type
        var genericMethod = enqueueWrapperMethod.MakeGenericMethod(jobType);

        // Create lambda: (TJob job) => job.ExecuteAsync(cancellationToken)
        var lambda = CreateExecutionExpression(jobType, executeMethod, cancellationToken);

        // Invoke our wrapper method which calls Hangfire API
        var jobId = (string?)genericMethod.Invoke(null, new object[] { lambda });

        _logger.LogInformation("Job {JobType} enqueued with Hangfire job ID: {JobId}",
            jobType.Name, jobId);

        return jobId;
    }

    /// <summary>
    /// Internal generic wrapper method that calls Hangfire's BackgroundJob.Enqueue API.
    /// This provides design-time validation that the Hangfire API exists and has correct signature.
    /// If Hangfire API changes, we'll get a compile error instead of runtime failure.
    /// Note: Queue name is not specified here - jobs will use the default queue or QueueAttribute if present.
    /// </summary>
    /// <typeparam name="T">The job type (must implement IRecurringJob)</typeparam>
    /// <param name="methodCall">Expression representing the job execution</param>
    /// <returns>Hangfire job ID</returns>
    private static string EnqueueJobInternal<T>(Expression<Func<T, Task>> methodCall)
        where T : IRecurringJob
    {
        return BackgroundJob.Enqueue(methodCall);
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
