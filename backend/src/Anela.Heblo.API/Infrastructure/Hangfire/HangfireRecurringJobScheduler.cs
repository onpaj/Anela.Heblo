using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

/// <summary>
/// Updates a Hangfire recurring job's CRON schedule live by delegating to
/// <see cref="HangfireJobRegistrationHelper"/> so the runtime-update path uses
/// the same registration code as startup discovery.
/// </summary>
public class HangfireRecurringJobScheduler : IHangfireRecurringJobScheduler
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

    public void UpdateCronSchedule(string jobName, string cronExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

        using var scope = _serviceProvider.CreateScope();
        var jobs = scope.ServiceProvider.GetServices<IRecurringJob>().ToList();
        var job = jobs.FirstOrDefault(j => j.Metadata.JobName == jobName);

        if (job == null)
        {
            _logger.LogWarning("Job {JobName} not found in DI — Hangfire schedule not updated live", jobName);
            return;
        }

        try
        {
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                job.GetType(),
                jobName,
                cronExpression,
                job.Metadata.TimeZoneId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update live Hangfire schedule for {JobName}. " +
                "TimeZone '{TimeZoneId}' may be invalid or unsupported on this host.",
                jobName, job.Metadata.TimeZoneId);
            return;
        }

        _logger.LogInformation(
            "Live Hangfire schedule updated for {JobName} → {CronExpression}",
            jobName, cronExpression);
    }
}
