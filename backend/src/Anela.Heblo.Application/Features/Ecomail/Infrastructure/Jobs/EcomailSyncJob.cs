using Anela.Heblo.Application.Features.Ecomail.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Ecomail;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Ecomail.Infrastructure.Jobs;

public sealed class EcomailSyncJob : IRecurringJob
{
    // A full run is ~400 API calls with Polly retry/backoff on throttling, so give it generous
    // headroom over the 6-hour schedule before another run is allowed to start.
    private const int LockTimeoutSeconds = 1800;

    /// <summary>Cap on errors echoed into the failure message persisted by Hangfire.</summary>
    private const int MaxReportedErrors = 10;

    private readonly IEcomailSyncService _syncService;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly EcomailOptions _options;
    private readonly ILogger<EcomailSyncJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public EcomailSyncJob(
        IEcomailSyncService syncService,
        IRecurringJobStatusChecker statusChecker,
        IOptions<EcomailOptions> options,
        ILogger<EcomailSyncJob> logger)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Metadata = new RecurringJobMetadata
        {
            JobName = "ecomail-sync",
            Category = RecurringJobCategory.Marketing,
            DisplayName = "Ecomail Sync",
            Description = "Pulls Ecomail campaign and automation statistics, and snapshots cumulative automation counters.",
            CronExpression = _options.CronExpression,
            // Always true: the seeder persists this as the job's default on first run, so tying it
            // to the API key would permanently record the job as disabled when seeded without one,
            // and it would never start once the secret is added. The empty-key check below is the
            // runtime no-op that protects a developer without credentials instead.
            DefaultIsEnabled = true,
            TimeZoneId = _options.TimeZone,
        };
    }

    [DisableConcurrentExecution(LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken, Metadata.DefaultIsEnabled))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogInformation("Job {JobName} has no API key configured. Skipping execution.", Metadata.JobName);
            return;
        }

        _logger.LogInformation("Job {JobName} started.", Metadata.JobName);

        var report = await _syncService.SyncAllAsync(cancellationToken);

        _logger.LogInformation(
            "Job {JobName} finished. Campaigns={Campaigns} Pipelines={Pipelines} CampaignStats={CampaignStats} Snapshots={Snapshots} Months={Months} Errors={Errors}",
            Metadata.JobName, report.CampaignsUpserted, report.PipelinesUpserted, report.CampaignStatsFetched,
            report.SnapshotsWritten, report.AutomationMonthsComputed, report.Errors.Count);

        // A healthy run against this account always captures something — 129 reportable campaigns
        // and 4 automations, with the current month always inside the recompute window — so zero
        // data means something is wrong even when nothing threw (e.g. every call returned an
        // empty/unexpected response without raising an error). Unlike the metadata counters above,
        // this no longer requires !report.IsFullSuccess: it fires whenever all three real-data
        // counters are zero, regardless of whether an exception was recorded. Caveat: this also
        // throws on every run against a genuinely empty Ecomail account — there is no such account
        // in production.
        if (report.CampaignStatsFetched == 0 && report.SnapshotsWritten == 0 && report.AutomationMonthsComputed == 0)
        {
            throw new InvalidOperationException(
                $"Ecomail sync produced no data: {Summarise(report.Errors)}");
        }

        // The guard above needs all three counters at zero, so a campaigns-stage failure hides
        // behind healthy snapshots and months: the run goes green having lost every newsletter.
        // That is not hypothetical — it is what happened on 2026-09-24, when one null field broke
        // the listing. Pipelines listing while campaigns yield nothing only happens when the
        // campaigns stage failed; this account has never had fewer than 250 campaigns.
        if (report.CampaignsUpserted == 0 && report.PipelinesUpserted > 0)
        {
            throw new InvalidOperationException(
                $"Ecomail sync listed {report.PipelinesUpserted} pipelines but no campaigns at all: " +
                Summarise(report.Errors));
        }
    }

    /// <summary>
    /// A total outage produces one error per campaign and per (pipeline x month) — hundreds of
    /// them. This message is persisted into Hangfire job state, so cap it; every individual error
    /// is already logged at its own call site.
    /// </summary>
    private static string Summarise(IReadOnlyList<string> errors)
    {
        if (errors.Count <= MaxReportedErrors)
        {
            return string.Join(" | ", errors);
        }

        return string.Join(" | ", errors.Take(MaxReportedErrors)) +
               $" | (+{errors.Count - MaxReportedErrors} more)";
    }
}
