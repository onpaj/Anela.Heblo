using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

/// <summary>
/// Updates a Hangfire recurring job's CRON schedule live by delegating to
/// <see cref="HangfireJobRegistrationHelper"/> so the runtime-update path uses
/// the same registration code as startup discovery.
/// The caller owns the time zone: it comes from the <c>RecurringJobConfiguration</c> DB row
/// and is never re-derived from <c>IRecurringJob.Metadata</c>. That is equivalent today because
/// <c>RecurringJobSeeder</c> re-syncs each row's <c>TimeZoneId</c> from metadata on every startup,
/// before <see cref="RecurringJobDiscoveryService"/> registers anything. If <c>TimeZoneId</c> ever
/// becomes admin-editable, the startup discovery path must switch to the DB value as well and the
/// seeder must stop re-syncing that field.
/// </summary>
public class HangfireRecurringJobScheduler : ICronScheduler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HangfireRecurringJobScheduler> _logger;

    public HangfireRecurringJobScheduler(
        IServiceProvider serviceProvider,
        ILogger<HangfireRecurringJobScheduler> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        // Resolves the runtime Type for jobName, which HangfireJobRegistrationHelper.RegisterOrUpdate
        // requires to close its generic RecurringJob.AddOrUpdate<TJob> overload. A scope is used (not a
        // direct IEnumerable<IRecurringJob> injection) because this adapter is a singleton and IRecurringJob
        // implementations are scoped. Job metadata is deliberately not read here — the schedule's time zone
        // comes from the caller.
        using var scope = _serviceProvider.CreateScope();
        var jobType = scope.ServiceProvider.GetServices<IRecurringJob>()
            .FirstOrDefault(j => j.Metadata.JobName == jobName)?.GetType();

        if (jobType == null)
        {
            _logger.LogWarning(
                "Job {JobName} has no registered IRecurringJob implementation in DI — " +
                "its Hangfire schedule was not updated live. The DB configuration row was still saved; " +
                "the new schedule will apply on the next application start if the job type is restored.",
                jobName);
            return;
        }

        try
        {
            HangfireJobRegistrationHelper.RegisterOrUpdate(jobType, jobName, cronExpression, timeZoneId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update live Hangfire schedule for {JobName}. " +
                "TimeZone '{TimeZoneId}' may be invalid or unsupported on this host.",
                jobName, timeZoneId);
            return;
        }

        _logger.LogInformation(
            "Live Hangfire schedule updated for {JobName} → {CronExpression} ({TimeZoneId})",
            jobName, cronExpression, timeZoneId);
    }
}
