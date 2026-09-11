using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Attendance.Infrastructure.Jobs;

public class AbsenceHoursJob : IRecurringJob
{
    private readonly AbsenceHoursService _service;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<AbsenceHoursJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "logeto-absence-hours",
        DisplayName = "Logeto — fill hours into timeless absences",
        Description = "Walks each opted-in worker's past days in Logeto (Výkaz práce) and writes their net " +
                      "daily contracted hours into every absence record entered with no From/To and no Hours.",
        CronExpression = "0 4 * * *",
        DefaultIsEnabled = false
    };

    public AbsenceHoursJob(
        AbsenceHoursService service,
        IRecurringJobStatusChecker statusChecker,
        ILogger<AbsenceHoursJob> logger)
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
