using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

/// <summary>
/// Service for triggering recurring jobs manually via Hangfire's RecurringJobManager.
/// </summary>
public class RecurringJobTriggerService : IRecurringJobTriggerService
{
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobTriggerService(
        IRecurringJobManager recurringJobManager,
        IRecurringJobStatusChecker statusChecker)
    {
        _recurringJobManager = recurringJobManager ?? throw new ArgumentNullException(nameof(recurringJobManager));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
    }

    /// <inheritdoc />
    public async Task<string?> TriggerJobAsync(string jobName, bool forceDisabled = false)
    {
        if (string.IsNullOrWhiteSpace(jobName))
        {
            throw new ArgumentException("Job name cannot be null or whitespace.", nameof(jobName));
        }

        // If forceDisabled is true, skip execution entirely and return null
        if (forceDisabled)
        {
            return null;
        }

        // Check if the job is enabled via status checker
        var isEnabled = await _statusChecker.IsJobEnabledAsync(jobName);
        if (!isEnabled)
        {
            return null;
        }

        // Trigger the recurring job immediately
        _recurringJobManager.Trigger(jobName);

        // Return the job name as the "job ID"
        // In Hangfire, recurring jobs are identified by their name/ID
        return jobName;
    }
}
