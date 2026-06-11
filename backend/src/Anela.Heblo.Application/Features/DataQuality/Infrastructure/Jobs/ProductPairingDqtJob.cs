using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;

public class ProductPairingDqtJob : IRecurringJob
{
    private readonly IDqtRunRepository _repository;
    private readonly IDriftDqtJobRunner _jobRunner;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<ProductPairingDqtJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-product-pairing-dqt",
        DisplayName = "Daily Product Pairing Data Quality Test",
        Description = "Compares product pairing between Shoptet and ABRA Flexi for the current day",
        CronExpression = "0 6 * * *", // Daily at 6:00 AM
        DefaultIsEnabled = true
    };

    public ProductPairingDqtJob(
        IDqtRunRepository repository,
        IDriftDqtJobRunner jobRunner,
        IRecurringJobStatusChecker statusChecker,
        ILogger<ProductPairingDqtJob> logger)
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

        var today = DateOnly.FromDateTime(DateTime.Today);

        _logger.LogInformation("Starting {JobName} for {Date}", Metadata.JobName, today);

        var run = DqtRun.Start(DqtTestType.ProductPairing, today, today, DqtTriggerType.Scheduled);
        await _repository.AddAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
    }
}
