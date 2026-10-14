using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Same replace-per-account-and-day semantics as <see cref="AdDailyFactUpserter"/>, keyed by ad group + term + match type.</summary>
public sealed class AdSearchTermUpserter
{
    private readonly AdsDbContext _db;
    private readonly TimeProvider _time;

    public AdSearchTermUpserter(AdsDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<AdUpsertCounts> ReplaceDayAsync(long accountId, DateOnly date, IReadOnlyList<AdSearchTermRow> rows,
        IReadOnlyDictionary<AdEntityKey, long> entityIds, CancellationToken ct)
    {
        var unmapped = 0;
        var incoming = new Dictionary<(long AdGroupId, string Term, string MatchType), AdSearchTermRow>();
        foreach (var row in rows.Where(r => r.Date == date && !string.IsNullOrEmpty(r.SearchTerm)))
        {
            if (!entityIds.TryGetValue(new AdEntityKey(AdEntityLevel.AdGroup, row.AdGroupExternalId), out var adGroupId))
            {
                unmapped++;
                continue;
            }
            var key = (adGroupId, row.SearchTerm, AdsDbValues.MatchTypeToDb(row.MatchType));
            incoming[key] = incoming.TryGetValue(key, out var previous) ? Sum(previous, row) : row;
        }

        var existing = await (
            from term in _db.SearchTermsDaily
            join entity in _db.Entities on term.AdGroupEntityId equals entity.Id
            where entity.AccountId == accountId && term.Date == date
            select term).ToListAsync(ct);
        var existingByKey = existing.ToDictionary(t => (t.AdGroupEntityId, t.SearchTerm, t.MatchType));

        foreach (var (key, row) in incoming)
        {
            if (!existingByKey.TryGetValue(key, out var term))
            {
                term = new AdSearchTermDaily { AdGroupEntityId = key.AdGroupId, Date = date, SearchTerm = key.Term, MatchType = key.MatchType };
                _db.SearchTermsDaily.Add(term);
            }
            term.Impressions = row.Impressions;
            term.Clicks = row.Clicks;
            term.Cost = row.Cost;
            term.Conversions = row.Conversions;
            term.ConversionValue = row.ConversionValue;
            term.Currency = row.Currency;
            term.SyncedAt = _time.GetUtcNow();
        }

        var stale = existing.Where(t => !incoming.ContainsKey((t.AdGroupEntityId, t.SearchTerm, t.MatchType))).ToList();
        _db.SearchTermsDaily.RemoveRange(stale);

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return new AdUpsertCounts(incoming.Count + stale.Count, unmapped);
    }

    private static AdSearchTermRow Sum(AdSearchTermRow a, AdSearchTermRow b) => a with
    {
        Impressions = a.Impressions + b.Impressions,
        Clicks = a.Clicks + b.Clicks,
        Cost = a.Cost + b.Cost,
        Conversions = a.Conversions + b.Conversions,
        ConversionValue = a.ConversionValue + b.ConversionValue,
    };
}
