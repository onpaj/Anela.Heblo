using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;

public class PriceComparisonDqtJob : IRecurringJob
{
    private readonly IDqtRunRepository _repository;
    private readonly IDriftDqtJobRunner _jobRunner;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PriceComparisonDqtJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-price-comparison-dqt",
        DisplayName = "Daily Price Comparison Data Quality Test",
        Description = "Compares retail prices between Shoptet (source of truth) and ABRA Flexi",
        // The other DQT checks are deliberately staggered (05:00 invoices, 06:00 product
        // pairing, 07:00 stock write-back, 08:00 lot stock); 06:00 collided exactly with
        // product pairing, so this one runs after all of them.
        CronExpression = "0 9 * * *", // Daily at 9:00 AM, after the other DQT checks
        DefaultIsEnabled = true
    };

    public PriceComparisonDqtJob(
        IDqtRunRepository repository,
        IDriftDqtJobRunner jobRunner,
        IRecurringJobStatusChecker statusChecker,
        TimeProvider timeProvider,
        ILogger<PriceComparisonDqtJob> logger)
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

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);

        _logger.LogInformation("Starting {JobName} for {Date}", Metadata.JobName, today);

        var run = DqtRun.Start(
            DqtTestType.PriceComparison, today, today, DqtTriggerType.Scheduled,
            _timeProvider.GetUtcNow().DateTime);
        await _repository.AddAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
    }
}
