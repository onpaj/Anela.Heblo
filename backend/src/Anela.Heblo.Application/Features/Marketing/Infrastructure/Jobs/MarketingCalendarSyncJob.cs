using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Marketing.Infrastructure.Jobs;

/// <summary>
/// Hourly Outlook → Heblo mirror of the marketing group calendar.
/// Outlook is the source of truth; see <see cref="IMarketingCalendarSyncService"/>.
/// </summary>
public class MarketingCalendarSyncJob : IRecurringJob
{
    internal const int PastDays = 30;
    internal const int FutureMonths = 12;

    private readonly IMarketingCalendarSyncService _syncService;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IOptions<MarketingCalendarOptions> _options;
    private readonly ILogger<MarketingCalendarSyncJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "marketing-calendar-sync",
        DisplayName = "Marketing — sync Outlook calendar",
        Description = "Hourly mirror of the Outlook marketing group calendar into Heblo: creates and updates actions from Outlook events and soft-deletes actions whose event was deleted in Outlook (each deletion confirmed with a direct Graph lookup). Window: 30 days back to 12 months ahead.",
        CronExpression = "0 * * * *",
        DefaultIsEnabled = true
    };

    public MarketingCalendarSyncJob(
        IMarketingCalendarSyncService syncService,
        IRecurringJobStatusChecker statusChecker,
        IOptions<MarketingCalendarOptions> options,
        ILogger<MarketingCalendarSyncJob> logger)
    {
        _syncService = syncService;
        _statusChecker = statusChecker;
        _options = options;
        _logger = logger;
    }

    private const int MarketingCalendarSyncLockTimeoutSeconds = 600;

    // A run can outlast the hourly cadence (one Graph GET per orphan over a 13-month
    // window), and two overlapping runs race on the same rows and on the unique
    // IX_MarketingActions_OutlookEventId index — which would fail the whole batch.
    [DisableConcurrentExecution(MarketingCalendarSyncLockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken, Metadata.DefaultIsEnabled))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Value.GroupId))
        {
            _logger.LogInformation("Job {JobName}: MarketingCalendar:GroupId is not configured. Skipping.", Metadata.JobName);
            return;
        }

        var now = DateTime.UtcNow;
        var fromUtc = now.AddDays(-PastDays);
        var toUtc = now.AddMonths(FutureMonths);

        _logger.LogInformation("Starting {JobName} for window {From:O} → {To:O}", Metadata.JobName, fromUtc, toUtc);

        var result = await _syncService.SyncAsync(fromUtc, toUtc, SyncActor.System, dryRun: false, cancellationToken);

        _logger.LogInformation(
            "{JobName} complete: {Created} created, {Updated} updated, {Deleted} deleted, {Skipped} skipped, {Failed} failed",
            Metadata.JobName, result.Created, result.Updated, result.Deleted, result.Skipped, result.Failed);

        if (result.UnmappedCategories.Count > 0)
        {
            _logger.LogWarning(
                "{JobName}: {Count} unmapped Outlook categor{Plural}: {Categories}",
                Metadata.JobName,
                result.UnmappedCategories.Count,
                result.UnmappedCategories.Count == 1 ? "y" : "ies",
                string.Join(", ", result.UnmappedCategories));
        }
    }
}
