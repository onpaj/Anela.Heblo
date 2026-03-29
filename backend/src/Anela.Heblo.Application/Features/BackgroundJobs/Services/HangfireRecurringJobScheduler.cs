using System.Reflection;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Updates a Hangfire recurring job's CRON schedule live using the same
/// reflection pattern as RecurringJobDiscoveryService.
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
        using var scope = _serviceProvider.CreateScope();
        var jobs = scope.ServiceProvider.GetServices<IRecurringJob>().ToList();
        var job = jobs.FirstOrDefault(j => j.Metadata.JobName == jobName);

        if (job == null)
        {
            _logger.LogWarning("Job {JobName} not found in DI — Hangfire schedule not updated live", jobName);
            return;
        }

        var jobType = job.GetType();
        var registerMethod = typeof(HangfireRecurringJobScheduler)
            .GetMethod(nameof(UpdateJobInternal), BindingFlags.NonPublic | BindingFlags.Static);

        if (registerMethod == null)
        {
            _logger.LogError("Could not find UpdateJobInternal method via reflection");
            return;
        }

        var genericMethod = registerMethod.MakeGenericMethod(jobType);
        genericMethod.Invoke(null, new object[] { jobName, cronExpression, job.Metadata.TimeZoneId });

        _logger.LogInformation(
            "Live Hangfire schedule updated for {JobName} → {CronExpression}",
            jobName, cronExpression);
    }

    private static void UpdateJobInternal<TJob>(
        string jobName,
        string cronExpression,
        string timeZoneId) where TJob : IRecurringJob
    {
        RecurringJob.AddOrUpdate<TJob>(
            jobName,
            j => j.ExecuteAsync(default),
            cronExpression,
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
    }
}
