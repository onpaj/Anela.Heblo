using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

/// <remarks>
/// Attempts = 0 follows the repo convention for nightly jobs (see ProductExportDownloadJob):
/// rethrow so the run is recorded Failed, but do not re-execute a job that talks to a live ERP on
/// a schedule nobody chose. The next scheduled run picks up from the watermark anyway.
/// </remarks>
[AutomaticRetry(Attempts = 0)]
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
            Category = RecurringJobCategory.Integrations,
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
                _logger.LogError(
                    "Job {JobName} completed with failures. TotalFetched={TotalFetched} TotalUpserted={TotalUpserted} FailedServices={FailedServices}",
                    Metadata.JobName,
                    report.TotalFetched,
                    report.TotalUpserted,
                    report.FailedServices);

                // Rethrow so Hangfire records the run as Failed. A failed entity is *handled* by
                // the orchestrator -- it is counted, not thrown -- so without this the job returns
                // normally and the dashboard shows green. That is exactly what happened on
                // 2026-09-24: all four entities failed, 1 260 ledger rows were lost, sync_state sat
                // on RUNNING for six hours, and Hangfire said Succeeded the whole time.
                throw new InvalidOperationException(
                    $"{Metadata.JobName} completed with {report.FailedServices} failed "
                    + $"{(report.FailedServices == 1 ? "entity" : "entities")}. "
                    + "See flexi_raw.sync_state for the per-entity status and error.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Hangfire cancels in-flight jobs on shutdown and redeploy. That is not a sync defect,
            // and recording it as one would train everyone to ignore the alert. Note the filter is
            // on the CALLER's token: a cancellation that came from our own RequestTimeoutSeconds
            // falls through to the catch below, because failing to finish in time is a real problem.
            _logger.LogInformation(
                "Job {JobName} was cancelled by the host (shutdown or redeploy).",
                Metadata.JobName);
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
