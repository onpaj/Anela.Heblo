using Anela.Heblo.Application.Features.Ecomail.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Ecomail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Ecomail.Infrastructure.Jobs;

public sealed class EcomailSyncJob : IRecurringJob
{
    private readonly IEcomailSyncService _syncService;
    private readonly EcomailOptions _options;
    private readonly ILogger<EcomailSyncJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public EcomailSyncJob(
        IEcomailSyncService syncService,
        IOptions<EcomailOptions> options,
        ILogger<EcomailSyncJob> logger)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Metadata = new RecurringJobMetadata
        {
            JobName = "ecomail-sync",
            DisplayName = "Ecomail Sync",
            Description = "Pulls Ecomail campaign and automation statistics, and snapshots cumulative automation counters.",
            CronExpression = _options.CronExpression,
            DefaultIsEnabled = !string.IsNullOrWhiteSpace(_options.ApiKey),
            TimeZoneId = _options.TimeZone,
        };
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
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

        // The run failed and nothing real landed — surface it to Hangfire rather than reporting
        // success. CampaignsUpserted/PipelinesUpserted are metadata counters: they increment as
        // soon as a campaign or pipeline is listed, before any stats call, so a run where every
        // stats/snapshot/event-count call fails would still show them as positive. Only counters
        // that represent actual statistics landing count as "data" here.
        if (!report.IsFullSuccess &&
            report.CampaignStatsFetched == 0 && report.SnapshotsWritten == 0 && report.AutomationMonthsComputed == 0)
        {
            throw new InvalidOperationException(
                $"Ecomail sync produced no data: {string.Join(" | ", report.Errors)}");
        }
    }
}
