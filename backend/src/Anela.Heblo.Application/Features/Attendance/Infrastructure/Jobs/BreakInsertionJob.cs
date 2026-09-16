using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Attendance.Infrastructure.Jobs;

public class BreakInsertionJob : IRecurringJob
{
    private readonly BreakInsertionService _service;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<BreakInsertionJob> _logger;

    /// <summary>Job identifier, shared with the on-demand entry point so the two cannot drift.</summary>
    public const string Name = "logeto-break-insertion";

    /// <summary>The job writes to a live external account, so an unseeded row must not enable it.</summary>
    public const bool DefaultEnabled = false;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = Name,
        DisplayName = "Logeto — insert missing lunch breaks",
        Description = "Walks each opted-in worker's days in Logeto (Výkaz práce) and inserts a 30-minute " +
                      "break into any ≥6h working day that has none, splitting the work record via " +
                      "merge=true and touching the result so phones pick the change up.",
        CronExpression = "0 3 * * *",
        DefaultIsEnabled = DefaultEnabled
    };

    public BreakInsertionJob(
        BreakInsertionService service,
        IRecurringJobStatusChecker statusChecker,
        ILogger<BreakInsertionJob> logger)
    {
        _service = service;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        _logger.LogInformation("Starting {JobName}", Metadata.JobName);
        await _service.RunAsync(cancellationToken);
    }
}
