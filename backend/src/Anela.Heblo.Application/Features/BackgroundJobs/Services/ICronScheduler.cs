namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Applies a CRON expression update to a running job schedule immediately,
/// without requiring a restart.
/// Fire-and-forget: implementations log and swallow scheduling failures (unknown job,
/// unresolvable time zone, storage error) rather than throwing. The one exception is
/// invalid arguments, which throw before any side effect occurs.
/// The caller owns the time zone: implementations must not re-derive it from any other
/// source, such as job metadata.
/// </summary>
public interface ICronScheduler
{
    /// <param name="jobName">Registered job name; also the Hangfire recurring-job id.</param>
    /// <param name="cronExpression">Validated 5- or 6-field CRON expression.</param>
    /// <param name="timeZoneId">System/IANA time-zone id the schedule is evaluated in; must be resolvable on the host via TimeZoneInfo.FindSystemTimeZoneById.</param>
    /// <exception cref="ArgumentException">Any argument is null, empty or whitespace.</exception>
    void UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId);
}
