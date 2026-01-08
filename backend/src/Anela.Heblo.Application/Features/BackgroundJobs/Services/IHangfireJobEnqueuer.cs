using Anela.Heblo.Domain.Features.BackgroundJobs;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Service responsible for enqueueing recurring jobs via Hangfire using reflection.
/// </summary>
public interface IHangfireJobEnqueuer
{
    /// <summary>
    /// Enqueues a recurring job for immediate execution using Hangfire's BackgroundJob.Enqueue.
    /// </summary>
    /// <param name="job">The recurring job instance to enqueue</param>
    /// <param name="cancellationToken">Cancellation token to pass to the job execution</param>
    /// <returns>Hangfire job ID if successful, null if enqueue failed</returns>
    string? EnqueueJob(IRecurringJob job, CancellationToken cancellationToken);
}
