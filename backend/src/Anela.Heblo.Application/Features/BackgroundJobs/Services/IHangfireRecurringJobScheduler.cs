namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Applies a CRON expression update to a running Hangfire job schedule immediately,
/// without requiring a restart.
/// </summary>
public interface IHangfireRecurringJobScheduler
{
    void UpdateCronSchedule(string jobName, string cronExpression);
}
