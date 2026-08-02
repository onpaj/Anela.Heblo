using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;

public class LotStockReconciliationDqtJob : IRecurringJob
{
    private readonly IDqtRunRepository _repository;
    private readonly IDriftDqtJobRunner _jobRunner;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LotStockReconciliationDqtJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-lot-stock-dqt",
        DisplayName = "Daily Lot/Stock Reconciliation Data Quality Test",
        Description = "Reconciles the sum of loaded stock lots against ERP on-hand stock for materials with expiration",
        CronExpression = "0 8 * * *", // Daily at 8:00 AM, after the earlier DQT checks
        DefaultIsEnabled = true
    };

    public LotStockReconciliationDqtJob(
        IDqtRunRepository repository,
        IDriftDqtJobRunner jobRunner,
        IRecurringJobStatusChecker statusChecker,
        TimeProvider timeProvider,
        ILogger<LotStockReconciliationDqtJob> logger)
    {
        _repository = repository;
        _jobRunner = jobRunner;
        _statusChecker = statusChecker;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        // Snapshot reconciliation — from/to are the current day for both bounds.
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);

        _logger.LogInformation("Starting {JobName} for {Date}", Metadata.JobName, today);

        var run = DqtRun.Start(DqtTestType.LotSumVsErpStock, today, today, DqtTriggerType.Scheduled);
        await _repository.AddAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
    }
}
