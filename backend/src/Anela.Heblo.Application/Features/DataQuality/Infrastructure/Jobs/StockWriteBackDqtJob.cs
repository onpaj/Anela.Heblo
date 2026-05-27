using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;

public class StockWriteBackDqtJob : IRecurringJob
{
    private readonly IDqtRunRepository _repository;
    private readonly IDriftDqtJobRunner _jobRunner;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<StockWriteBackDqtJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-stock-writeback-dqt",
        DisplayName = "Daily Stock Write-Back Data Quality Test",
        Description = "Reconciles stock write-back between Shoptet and ABRA Flexi for the previous day",
        CronExpression = "0 7 * * *", // Daily at 7:00 AM
        DefaultIsEnabled = true
    };

    public StockWriteBackDqtJob(
        IDqtRunRepository repository,
        IDriftDqtJobRunner jobRunner,
        IRecurringJobStatusChecker statusChecker,
        ILogger<StockWriteBackDqtJob> logger)
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

        var run = DqtRun.Start(DqtTestType.StockWriteBackReconciliation, yesterday, yesterday, DqtTriggerType.Scheduled);
        await _repository.AddAsync(run, cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
    }
}
