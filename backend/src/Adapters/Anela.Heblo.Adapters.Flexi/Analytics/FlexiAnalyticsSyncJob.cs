using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public sealed class FlexiAnalyticsSyncJob : IRecurringJob
{
    private readonly IFlexiAnalyticsSyncService _syncService;
    private readonly FlexiAnalyticsSyncOptions _options;
    private readonly ILogger<FlexiAnalyticsSyncJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public FlexiAnalyticsSyncJob(
        IFlexiAnalyticsSyncService syncService,
        IOptions<FlexiAnalyticsSyncOptions> options,
        ILogger<FlexiAnalyticsSyncJob> logger)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Metadata = new RecurringJobMetadata
        {
            JobName = "flexi-analytics-sync",
            DisplayName = "Flexi Analytics Sync",
            Description = "Syncs Flexi ERP analytics data (ledger, departments, accounting templates, contacts) into the analytics schema.",
            CronExpression = _options.CronExpression,
            DefaultIsEnabled = _options.Enabled,
            TimeZoneId = _options.TimeZone,
        };
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Job {JobName} is disabled via configuration. Skipping execution.", Metadata.JobName);
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

        try
        {
            _logger.LogInformation("Job {JobName} started.", Metadata.JobName);

            var report = await _syncService.SyncAllAsync(timeoutCts.Token);

            if (report.IsFullSuccess)
            {
                _logger.LogInformation(
                    "Job {JobName} completed successfully. TotalFetched={TotalFetched} TotalUpserted={TotalUpserted} FailedServices={FailedServices}",
                    Metadata.JobName,
                    report.TotalFetched,
                    report.TotalUpserted,
                    report.FailedServices);
            }
            else
            {
                _logger.LogWarning(
                    "Job {JobName} completed with failures. TotalFetched={TotalFetched} TotalUpserted={TotalUpserted} FailedServices={FailedServices}",
                    Metadata.JobName,
                    report.TotalFetched,
                    report.TotalUpserted,
                    report.FailedServices);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Job {JobName} failed with an unhandled exception.",
                Metadata.JobName);
            throw;
        }
    }
}
