using System.Reflection;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Single entry point for binding a runtime <see cref="Type"/> to
/// <see cref="RecurringJob.AddOrUpdate{TJob}(string, System.Linq.Expressions.Expression{Action{TJob}}, string, RecurringJobOptions)"/>.
/// Used by both startup discovery and runtime CRON updates so that both code paths
/// produce identical Hangfire <c>RecurringJob</c> records.
/// </summary>
public static class HangfireJobRegistrationHelper
{
    /// <summary>
    /// Registers or updates a Hangfire recurring job for the given runtime job type.
    /// Always uses the <see cref="RecurringJobOptions"/> overload.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="jobType"/> is null.</exception>
    /// <exception cref="ArgumentException">A string argument is null/empty/whitespace, or <paramref name="jobType"/> does not implement <see cref="IRecurringJob"/>.</exception>
    /// <exception cref="TimeZoneNotFoundException">The time zone id is not resolvable on this host.</exception>
    public static void RegisterOrUpdate(
        Type jobType,
        string jobName,
        string cronExpression,
        string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(jobType);

        if (string.IsNullOrWhiteSpace(jobName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(jobName));
        }

        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(cronExpression));
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(timeZoneId));
        }

        if (!typeof(IRecurringJob).IsAssignableFrom(jobType))
        {
            throw new ArgumentException(
                $"{jobType.FullName} does not implement {nameof(IRecurringJob)}.",
                nameof(jobType));
        }

        var dispatcher = typeof(HangfireJobRegistrationHelper)
            .GetMethod(nameof(RegisterOrUpdateGeneric), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Could not resolve {nameof(RegisterOrUpdateGeneric)} via reflection.");

        var closed = dispatcher.MakeGenericMethod(jobType);

        try
        {
            _ = closed.Invoke(null, new object[] { jobName, cronExpression, timeZoneId });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Surface the real cause (e.g. TimeZoneNotFoundException) instead of the
            // reflection wrapper, so callers can log structured context.
            throw ex.InnerException;
        }
    }

    private static void RegisterOrUpdateGeneric<TJob>(
        string jobName,
        string cronExpression,
        string timeZoneId)
        where TJob : IRecurringJob
    {
        RecurringJob.AddOrUpdate<TJob>(
            jobName,
            job => job.ExecuteAsync(default),
            cronExpression,
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)
            });
    }
}
