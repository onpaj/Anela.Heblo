using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

public sealed class ShoptetOrdersSyncJob : IRecurringJob
{
    private readonly IShoptetOrdersSyncService _syncService;
    private readonly ShoptetOrdersSyncOptions _options;
    private readonly ILogger<ShoptetOrdersSyncJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public ShoptetOrdersSyncJob(
        IShoptetOrdersSyncService syncService,
        IOptions<ShoptetOrdersSyncOptions> options,
        ILogger<ShoptetOrdersSyncJob> logger)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Metadata = new RecurringJobMetadata
        {
            JobName = "shoptet-orders-sync",
            DisplayName = "Shoptet Orders Sync",
            Description = "Mirrors Shoptet order headers and line items into the shoptet_raw schema for reporting.",
            CronExpression = _options.CronExpression,
            DefaultIsEnabled = _options.Enabled,
            TimeZoneId = _options.TimeZone,
            Category = RecurringJobCategory.Integrations,
        };
    }

    // A retry would re-run up to a 4-hour backfill in business hours, on the API token and vCore the
    // packing flow shares. The job is resumable, so tomorrow night's run picks up where this one stopped.
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Job {JobName} is disabled via configuration. Skipping execution.", Metadata.JobName);
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.JobTimeoutSeconds));

        try
        {
            _logger.LogInformation("Job {JobName} started.", Metadata.JobName);

            var report = await _syncService.SyncAsync(timeoutCts.Token);

            if (report.IsFullSuccess)
            {
                _logger.LogInformation(
                    "Job {JobName} completed successfully. TotalFetched={TotalFetched} TotalUpserted={TotalUpserted} BackfillCompleted={BackfillCompleted}",
                    Metadata.JobName, report.TotalFetched, report.TotalUpserted, report.BackfillCompleted);
            }
            else
            {
                _logger.LogWarning(
                    "Job {JobName} completed with failures. TotalFetched={TotalFetched} TotalUpserted={TotalUpserted} BackfillCompleted={BackfillCompleted}",
                    Metadata.JobName, report.TotalFetched, report.TotalUpserted, report.BackfillCompleted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobName} failed with an unhandled exception.", Metadata.JobName);
            throw;
        }
    }
}
