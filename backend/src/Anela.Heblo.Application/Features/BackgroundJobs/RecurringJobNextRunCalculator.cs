using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;
using NCrontab.Advanced;
using NCrontab.Advanced.Exceptions;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

/// <summary>
/// Computes the next UTC run time of a recurring job from its CRON expression,
/// evaluated in the configured job timezone. Returns null when the job is
/// disabled, the timezone is unavailable, or the CRON expression is invalid.
/// </summary>
public static class RecurringJobNextRunCalculator
{
    public static DateTime? Calculate(string cronExpression, bool isEnabled, DateTime utcNow, ILogger logger, string? jobName = null)
    {
        if (!isEnabled)
        {
            return null;
        }

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(RecurringJobMetadata.DefaultTimeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            logger.LogWarning(ex, "Timezone '{TimeZoneId}' not found on host, NextRunAt will be null for job '{JobName}'",
                RecurringJobMetadata.DefaultTimeZoneId, jobName);
            return null;
        }

        try
        {
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
            var nextLocal = CrontabSchedule.Parse(cronExpression).GetNextOccurrence(nowLocal);
            var nextUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), tz);
            return DateTime.SpecifyKind(nextUtc, DateTimeKind.Utc);
        }
        catch (CrontabException ex)
        {
            logger.LogWarning(ex, "Invalid CRON expression '{CronExpression}' for job '{JobName}', NextRunAt will be null",
                cronExpression, jobName);
            return null;
        }
    }
}
