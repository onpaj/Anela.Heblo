namespace Anela.Heblo.Domain.Features.BackgroundJobs;

/// <summary>
/// Service for manually triggering recurring jobs on-demand
/// </summary>
public interface IRecurringJobTriggerService
{
    /// <summary>
    /// Trigger a recurring job immediately (fire-and-forget)
    /// </summary>
    /// <param name="jobName">The job name to trigger</param>
    /// <param name="forceDisabled">If true, triggers even if job is disabled</param>
    /// <returns>Job ID from Hangfire, or null if job not found</returns>
    Task<string?> TriggerJobAsync(string jobName, bool forceDisabled = false);
}
