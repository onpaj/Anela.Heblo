using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// One-off historical load of daily facts and search terms, run by a person through
/// backend/tools/Anela.Heblo.AdsBackfill — never by a job. Per managed account: entities first (facts
/// need their ids), then one calendar month at a time, each day replaced exactly as the daily run
/// replaces it, so re-running any range is safe. A source implementing
/// <see cref="IAdPlatformRangeReadSource"/> is read a month per call; others day by day.
/// Leaves the DailyFacts/SearchTerms watermarks alone: they belong to the daily job.
/// An account stops at its first failed month and the error names the date to resume from.
/// </summary>
public sealed class AdsBackfillService
{
    private readonly IEnumerable<IAdPlatformReadSource> _sources;
    private readonly AdAccountUpserter _accounts;
    private readonly AdEntitySyncStep _entities;
    private readonly AdDailyFactUpserter _facts;
    private readonly AdSearchTermUpserter _searchTerms;
    private readonly AdsDbContext _db;
    private readonly TimeProvider _time;
    private readonly ILogger<AdsBackfillService> _logger;

    public AdsBackfillService(IEnumerable<IAdPlatformReadSource> sources, AdAccountUpserter accounts, AdEntitySyncStep entities,
        AdDailyFactUpserter facts, AdSearchTermUpserter searchTerms, AdsDbContext db, TimeProvider time, ILogger<AdsBackfillService> logger)
    {
        _sources = sources;
        _accounts = accounts;
        _entities = entities;
        _facts = facts;
        _searchTerms = searchTerms;
        _db = db;
        _time = time;
        _logger = logger;
    }

    public async Task<AdsSyncReport> RunAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (from > to)
            throw new ArgumentException($"'from' ({from:yyyy-MM-dd}) is after 'to' ({to:yyyy-MM-dd}).", nameof(from));

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
                var (written, error) = await BackfillAccountAsync(source, account, from, to, ct);
                rows += written;
                if (error is not null)
                    errors.Add(error);
            }
        }

        _logger.LogInformation("AdsBackfill.Completed from={From:yyyy-MM-dd} to={To:yyyy-MM-dd} rows={Rows} errors={Errors}", from, to, rows, errors.Count);
        return new AdsSyncReport(false, null, sources.Count, rows, errors);
    }

    private async Task<(int Rows, string? Error)> BackfillAccountAsync(IAdPlatformReadSource source, AdAccount account,
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        var label = $"{source.Platform}/{account.ExternalId}";
        var today = AdSyncDates.Today(_time.GetUtcNow(), account.TimeZone);
        if (to >= today)
            return (0, $"{label}: 'to' ({to:yyyy-MM-dd}) must be before the account's today ({today:yyyy-MM-dd}); a day still running has incomplete data.");

        var entities = await _entities.RunAsync(source, account, recordSnapshotDiffs: !source.Capabilities.ChangeLog, ct);
        if (!entities.Outcome.Succeeded)
            return (0, entities.Outcome.Error);

        var rows = 0;
        foreach (var (chunkFrom, chunkTo) in AdSyncDates.MonthChunks(from, to))
        {
            try
            {
                rows += await BackfillChunkAsync(source, account, chunkFrom, chunkTo, entities.EntityIds, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _db.ChangeTracker.Clear();
                _logger.LogError(ex, "AdsBackfill.ChunkFailed {Platform} {Account} {From:yyyy-MM-dd}..{To:yyyy-MM-dd}", source.Platform, account.ExternalId, chunkFrom, chunkTo);
                return (rows, $"{label}: {chunkFrom:yyyy-MM-dd}..{chunkTo:yyyy-MM-dd} failed ({ex.Message}); resume from {chunkFrom:yyyy-MM-dd}.");
            }
        }
        return (rows, null);
    }

    private async Task<int> BackfillChunkAsync(IAdPlatformReadSource source, AdAccount account, DateOnly from, DateOnly to,
        IReadOnlyDictionary<AdEntityKey, long> entityIds, CancellationToken ct)
    {
        var days = AdSyncDates.Days(from, to);
        var range = source as IAdPlatformRangeReadSource;
        var accountId = account.ExternalId;

        var factRows = await FetchAsync(days,
            range is null ? null : () => range.GetDailyFactsAsync(accountId, from, to, ct),
            day => source.GetDailyFactsAsync(accountId, day, ct));
        var facts = await ReplaceDaysAsync(days, factRows, r => r.Date,
            (day, dayRows) => _facts.ReplaceDayAsync(account.Id, day, dayRows, entityIds, ct));

        var terms = new AdUpsertCounts(0, 0);
        if (source.Capabilities.SearchTerms)
        {
            var termRows = await FetchAsync(days,
                range is null ? null : () => range.GetSearchTermsAsync(accountId, from, to, ct),
                day => source.GetSearchTermsAsync(accountId, day, ct));
            terms = await ReplaceDaysAsync(days, termRows, r => r.Date,
                (day, dayRows) => _searchTerms.ReplaceDayAsync(account.Id, day, dayRows, entityIds, ct));
        }

        _logger.LogInformation(
            "AdsBackfill.Month {Platform} {Account} {From:yyyy-MM-dd}..{To:yyyy-MM-dd} facts={Facts} searchTerms={SearchTerms}",
            source.Platform, accountId, from, to, facts.Written, terms.Written);
        // History for an entity the current listing does not return is lost, not deferred: say so loudly.
        if (facts.Unmapped + terms.Unmapped > 0)
            _logger.LogWarning(
                "AdsBackfill.UnmappedRows {Platform} {Account} {From:yyyy-MM-dd}..{To:yyyy-MM-dd} facts={Facts} searchTerms={SearchTerms} - skipped, their entities are not in the entity listing",
                source.Platform, accountId, from, to, facts.Unmapped, terms.Unmapped);
        return facts.Written + terms.Written;
    }

    private static async Task<IReadOnlyList<TRow>> FetchAsync<TRow>(IReadOnlyList<DateOnly> days,
        Func<Task<IReadOnlyList<TRow>>>? fetchRange, Func<DateOnly, Task<IReadOnlyList<TRow>>> fetchDay)
    {
        if (fetchRange is not null)
            return await fetchRange();

        var rows = new List<TRow>();
        foreach (var day in days)
            rows.AddRange(await fetchDay(day));
        return rows;
    }

    /// <summary>Every day of the chunk is replaced, including days without rows, so stale rows are removed there too.</summary>
    private static async Task<AdUpsertCounts> ReplaceDaysAsync<TRow>(IReadOnlyList<DateOnly> days, IReadOnlyList<TRow> rows,
        Func<TRow, DateOnly> dateOf, Func<DateOnly, IReadOnlyList<TRow>, Task<AdUpsertCounts>> replaceDay)
    {
        var byDay = rows.ToLookup(dateOf);
        var written = 0;
        var unmapped = 0;
        foreach (var day in days)
        {
            var counts = await replaceDay(day, byDay[day].ToList());
            written += counts.Written;
            unmapped += counts.Unmapped;
        }
        return new AdUpsertCounts(written, unmapped);
    }
}
