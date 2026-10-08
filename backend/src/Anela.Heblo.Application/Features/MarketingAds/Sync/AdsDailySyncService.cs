using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// Daily backbone run (spec section 5): for every registered source and managed account —
/// entities, then daily facts and search terms for yesterday plus FactLookbackDays.
/// Sequential on purpose: the destination is a single-vCore burstable Postgres.
/// </summary>
public sealed class AdsDailySyncService : IAdsDailySyncService
{
    private readonly IEnumerable<IAdPlatformReadSource> _sources;
    private readonly AdAccountUpserter _accounts;
    private readonly AdEntitySyncStep _entities;
    private readonly AdDailyFactUpserter _facts;
    private readonly AdSearchTermUpserter _searchTerms;
    private readonly AdStreamRunner _runner;
    private readonly TimeProvider _time;
    private readonly AdsSyncOptions _options;
    private readonly ILogger<AdsDailySyncService> _logger;

    public AdsDailySyncService(IEnumerable<IAdPlatformReadSource> sources, AdAccountUpserter accounts, AdEntitySyncStep entities,
        AdDailyFactUpserter facts, AdSearchTermUpserter searchTerms, AdStreamRunner runner, TimeProvider time,
        IOptions<AdsSyncOptions> options, ILogger<AdsDailySyncService> logger)
    {
        _sources = sources;
        _accounts = accounts;
        _entities = entities;
        _facts = facts;
        _searchTerms = searchTerms;
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
                var outcomes = await SyncAccountAsync(source, account, ct);
                rows += outcomes.Sum(o => o.RowsWritten);
                errors.AddRange(outcomes.Where(o => !o.Succeeded).Select(o => o.Error!));
            }
        }

        _logger.LogInformation("AdsDailySync.Completed sources={Sources} rows={Rows} errors={Errors}", sources.Count, rows, errors.Count);
        return new AdsSyncReport(false, null, sources.Count, rows, errors);
    }

    private async Task<IReadOnlyList<AdStreamOutcome>> SyncAccountAsync(IAdPlatformReadSource source, AdAccount account, CancellationToken ct)
    {
        var entities = await _entities.RunAsync(source, account, recordSnapshotDiffs: !source.Capabilities.ChangeLog, ct);
        if (!entities.Outcome.Succeeded)
            return new[] { entities.Outcome };

        var yesterday = AdSyncDates.Yesterday(_time.GetUtcNow(), account.TimeZone);
        var dates = AdSyncDates.FactWindow(yesterday, _options.FactLookbackDays);
        var watermark = new DateTimeOffset(yesterday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var outcomes = new List<AdStreamOutcome> { entities.Outcome };

        outcomes.Add(await _runner.RunAsync(source.Platform, account.ExternalId, AdSyncStreams.DailyFacts, async token =>
        {
            var written = 0;
            var unmapped = 0;
            foreach (var date in dates)
            {
                var counts = await _facts.ReplaceDayAsync(account.Id, date,
                    await source.GetDailyFactsAsync(account.ExternalId, date, token), entities.EntityIds, token);
                written += counts.Written;
                unmapped += counts.Unmapped;
            }
            LogUnmapped(source.Platform, account.ExternalId, AdSyncStreams.DailyFacts, unmapped);
            return new AdStreamWork(written, watermark);
        }, ct));

        if (!source.Capabilities.SearchTerms)
            return outcomes;

        outcomes.Add(await _runner.RunAsync(source.Platform, account.ExternalId, AdSyncStreams.SearchTerms, async token =>
        {
            var written = 0;
            var unmapped = 0;
            foreach (var date in dates)
            {
                var counts = await _searchTerms.ReplaceDayAsync(account.Id, date,
                    await source.GetSearchTermsAsync(account.ExternalId, date, token), entities.EntityIds, token);
                written += counts.Written;
                unmapped += counts.Unmapped;
            }
            LogUnmapped(source.Platform, account.ExternalId, AdSyncStreams.SearchTerms, unmapped);
            return new AdStreamWork(written, watermark);
        }, ct));

        return outcomes;
    }

    private void LogUnmapped(AdPlatform platform, string account, string stream, int unmapped)
    {
        if (unmapped > 0)
            _logger.LogWarning("AdsDailySync.UnmappedRows {Platform} {Account} {Stream} count={Count} - rows for entities the entity listing did not return",
                platform, account, stream, unmapped);
    }
}
