using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;

public class InvoiceDqtJob : IRecurringJob
{
    private readonly IInvoiceDqtJobRunner _jobRunner;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<InvoiceDqtJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "weekly-invoice-dqt",
        DisplayName = "Weekly Invoice DQT (Shoptet ↔ Flexi)",
        Description = "Compares issued invoices between Shoptet and ABRA Flexi for the previous complete week (Mon–Sun)",
        CronExpression = "0 21 * * 1", // Monday at 21:00 UTC = 23:00 CEST
        DefaultIsEnabled = true
    };

    public InvoiceDqtJob(
        IInvoiceDqtJobRunner jobRunner,
        IRecurringJobStatusChecker statusChecker,
        ILogger<InvoiceDqtJob> logger)
    {
        _jobRunner = jobRunner;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var daysToLastMonday = ((int)today.DayOfWeek + 6) % 7 + 7;
        var lastMonday = today.AddDays(-daysToLastMonday);
        var lastSunday = lastMonday.AddDays(6);

        _logger.LogInformation("Starting {JobName} for week {DateFrom} to {DateTo}",
            Metadata.JobName, lastMonday, lastSunday);

        await _jobRunner.RunForDateRangeAsync(lastMonday, lastSunday, DqtTriggerType.Scheduled, cancellationToken);
    }
}
