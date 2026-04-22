using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;

public class InvoiceDqtJob : IRecurringJob
{
    private readonly IDqtRunRepository _repository;
    private readonly IInvoiceDqtJobRunner _jobRunner;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<InvoiceDqtJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-invoice-dqt",
        DisplayName = "Daily Invoice Data Quality Test",
        Description = "Compares issued invoices between Shoptet and ABRA Flexi for the previous day",
        CronExpression = "0 5 * * *", // Daily at 5:00 AM
        DefaultIsEnabled = true
    };

    public InvoiceDqtJob(
        IDqtRunRepository repository,
        IInvoiceDqtJobRunner jobRunner,
        IRecurringJobStatusChecker statusChecker,
        ILogger<InvoiceDqtJob> logger)
    {
        _repository = repository;
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

        var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));

        _logger.LogInformation("Starting {JobName} for {Date}", Metadata.JobName, yesterday);

        var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Scheduled);
        await _repository.AddAsync(run, cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
    }
}
