using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// Hourly change detection (spec section 5). Sources with a change log: pull events since the
/// watermark. Sources without one: re-list entities and record snapshot diffs.
/// </summary>
public sealed class AdsChangeSyncService : IAdsChangeSyncService
{
    private readonly IEnumerable<IAdPlatformReadSource> _sources;
    private readonly AdAccountUpserter _accounts;
    private readonly AdEntitySyncStep _entityStep;
    private readonly AdEntityUpserter _entities;
    private readonly AdChangeEventWriter _writer;
    private readonly IAdSyncStateStore _state;
    private readonly AdStreamRunner _runner;
    private readonly TimeProvider _time;
    private readonly AdsSyncOptions _options;
    private readonly ILogger<AdsChangeSyncService> _logger;

    public AdsChangeSyncService(IEnumerable<IAdPlatformReadSource> sources, AdAccountUpserter accounts, AdEntitySyncStep entityStep,
        AdEntityUpserter entities, AdChangeEventWriter writer, IAdSyncStateStore state, AdStreamRunner runner, TimeProvider time,
        IOptions<AdsSyncOptions> options, ILogger<AdsChangeSyncService> logger)
    {
        _sources = sources;
        _accounts = accounts;
        _entityStep = entityStep;
        _entities = entities;
        _writer = writer;
        _state = state;
        _runner = runner;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AdsSyncReport> RunAsync(CancellationToken ct)
    {
        _options.EnsureValid();
        var sources = _sources.ToList();
        if (sources.Count == 0)
            return AdsSyncReport.Skip("No ad platform read source is registered.");

        var errors = new List<string>();
        var rows = 0;
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            var managed = await _accounts.SyncManagedAsync(source, ct);
            if (managed.Error is not null)
            {
                errors.Add(managed.Error);
                continue;
            }

            foreach (var account in managed.Accounts)
            {
                var outcome = source.Capabilities.ChangeLog
                    ? await PullChangeLogAsync(source, account, ct)
                    : (await _entityStep.RunAsync(source, account, recordSnapshotDiffs: true, ct)).Outcome;
                rows += outcome.RowsWritten;
                if (!outcome.Succeeded)
                    errors.Add(outcome.Error!);
            }
        }

        _logger.LogInformation("AdsChangeSync.Completed sources={Sources} rows={Rows} errors={Errors}", sources.Count, rows, errors.Count);
        return new AdsSyncReport(false, null, sources.Count, rows, errors);
    }

    /// <summary>
    /// Watermark rule: the stored watermark is the newest <c>OccurredAt</c> the source returned, and
    /// <c>since</c> is passed to the source raw — C2 never clamps it. An empty pull returns a null
    /// watermark, which keeps the stored one; the first pull reaches back <c>InitialLookbackDays</c>.
    /// Why: the source owns its history limit (Google clamps DST-aware and logs a warning when
    /// <c>since</c> is too old), <c>since</c> is inclusive, and repeats are dropped by
    /// <c>ExternalEventId</c> (writer + unique key), so the newest event is re-read harmlessly. That
    /// is the only rule under which the source's 10 000-row cut is lossless: the next run continues
    /// from the newest event returned. Max, not Last, because only Google promises ascending order.
    /// </summary>
    private async Task<AdStreamOutcome> PullChangeLogAsync(IAdPlatformReadSource source, AdAccount account, CancellationToken ct)
    {
        var state = await _state.GetAsync(source.Platform, account.ExternalId, AdSyncStreams.ChangeEvents, ct);
        var since = state?.Watermark ?? _time.GetUtcNow() - TimeSpan.FromDays(_options.ChangeSync.InitialLookbackDays);

        return await _runner.RunAsync(source.Platform, account.ExternalId, AdSyncStreams.ChangeEvents, async token =>
        {
            var rows = await source.GetChangeEventsAsync(account.ExternalId, since, token);
            var ids = await _entities.LoadIdsAsync(account.Id, token);
            var written = await _writer.WriteAsync(source.Platform, account, AdChangeSource.PlatformChangeLog,
                rows.Select(AdChangeEventCandidate.FromRow).ToList(), ids, token);
            DateTimeOffset? watermark = rows.Count == 0 ? null : rows.Max(r => r.OccurredAt).ToUniversalTime();
            return new AdStreamWork(written, watermark);
        }, ct);
    }
}
