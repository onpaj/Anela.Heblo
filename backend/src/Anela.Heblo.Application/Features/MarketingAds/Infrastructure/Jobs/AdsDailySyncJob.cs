using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;

public sealed class AdsDailySyncJob : IRecurringJob
{
    private const int LockTimeoutSeconds = 3600;

    private readonly IAdsDailySyncService _sync;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly AdsSyncOptions _options;
    private readonly ILogger<AdsDailySyncJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public AdsDailySyncJob(IAdsDailySyncService sync, IRecurringJobStatusChecker statusChecker,
        IOptions<AdsSyncOptions> options, ILogger<AdsDailySyncJob> logger)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // No validation here: job discovery constructs every job, and a throw would unschedule all of them.
        Metadata = new RecurringJobMetadata
        {
            JobName = "ads-daily-sync",
            DisplayName = "Ads Daily Sync",
            Description = "Pulls ad accounts, campaigns/ad groups/keywords/ads, daily cost and conversion facts (yesterday plus a lookback window) and search terms from every configured ad platform into the ads schema.",
            CronExpression = _options.DailySync.CronExpression,
            // Always true: the runtime no-op when no database or no platform is configured protects unconfigured environments.
            DefaultIsEnabled = true,
            TimeZoneId = _options.DailySync.TimeZone,
            Category = RecurringJobCategory.Marketing,
        };
    }

    [DisableConcurrentExecution(LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken, Metadata.DefaultIsEnabled))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(_options.DailySync.RunTimeoutMinutes));

        _logger.LogInformation("Job {JobName} started.", Metadata.JobName);
        var report = await _sync.RunAsync(timeout.Token);

        if (report.Skipped)
        {
            _logger.LogInformation("Job {JobName} skipped: {Reason}", Metadata.JobName, report.SkipReason);
            return;
        }

        _logger.LogInformation("Job {JobName} finished. Sources={Sources} Rows={Rows} Errors={Errors}",
            Metadata.JobName, report.SourcesAttempted, report.RowsWritten, report.Errors.Count);
        if (report.HasFailures)
            throw AdsSyncJobFailure.From(Metadata.JobName, report);
    }
}
