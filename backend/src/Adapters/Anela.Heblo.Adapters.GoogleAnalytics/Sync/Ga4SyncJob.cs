using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

public sealed class Ga4SyncJob : IRecurringJob
{
    private readonly IGa4SyncService _syncService;
    private readonly Ga4SyncOptions _options;
    private readonly ILogger<Ga4SyncJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public Ga4SyncJob(
        IGa4SyncService syncService,
        IOptions<Ga4SyncOptions> options,
        ILogger<Ga4SyncJob> logger)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Metadata = new RecurringJobMetadata
        {
            JobName = "ga4-aggregates-sync",
            DisplayName = "GA4 Aggregates Sync",
            Description = "Pulls aggregated Google Analytics 4 traffic, landing pages, pages and e-commerce conversions into the ga4_agg schema, re-pulling a trailing window because GA4 keeps revising recent days.",
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
                    "Job {JobName} completed successfully. TotalFetched={TotalFetched} TotalUpserted={TotalUpserted}",
                    Metadata.JobName, report.TotalFetched, report.TotalUpserted);
            }
            else
            {
                _logger.LogWarning(
                    "Job {JobName} completed with failures. TotalFetched={TotalFetched} TotalUpserted={TotalUpserted} FailedServices={FailedServices}",
                    Metadata.JobName, report.TotalFetched, report.TotalUpserted, report.FailedServices);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobName} failed with an unhandled exception.", Metadata.JobName);
            throw;
        }
    }
}
