namespace Anela.Heblo.Domain.Features.BackgroundJobs;

public interface IRecurringJobStatusChecker
{
    /// <summary>
    /// Returns whether the recurring job named <paramref name="jobName"/> is enabled.
    /// </summary>
    /// <param name="jobName">Unique job identifier matching <see cref="RecurringJobMetadata.JobName"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="defaultIfMissing">
    /// Value to return when no configuration row exists for the job.
    /// Defaults to <c>true</c> to preserve historical fail-open behavior for existing callers.
    /// Pass <see cref="RecurringJobMetadata.DefaultIsEnabled"/> for jobs that must default disabled
    /// (e.g. LLM-cost-producing jobs) so an unseeded row does not silently enable them.
    /// </param>
    Task<bool> IsJobEnabledAsync(
        string jobName,
        CancellationToken cancellationToken = default,
        bool defaultIfMissing = true);
}
