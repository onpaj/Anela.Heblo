using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>
/// Makes the stored facts of one account and day equal to what the platform reports now —
/// late conversions update rows, and a row the platform no longer reports is deleted rather
/// than left stale. Load + RemoveRange (not ExecuteDelete) so EF InMemory tests exercise it.
/// </summary>
public sealed class AdDailyFactUpserter
{
    private readonly AdsDbContext _db;
    private readonly TimeProvider _time;

    public AdDailyFactUpserter(AdsDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<AdUpsertCounts> ReplaceDayAsync(long accountId, DateOnly date, IReadOnlyList<AdDailyFactRow> rows,
        IReadOnlyDictionary<AdEntityKey, long> entityIds, CancellationToken ct)
    {
        var unmapped = 0;
        var incoming = new Dictionary<long, AdDailyFactRow>();
        foreach (var row in rows.Where(r => r.Date == date))
        {
            if (!entityIds.TryGetValue(new AdEntityKey(row.Level, row.EntityExternalId), out var entityId))
            {
                unmapped++;
                continue;
            }
            incoming[entityId] = incoming.TryGetValue(entityId, out var previous) ? Sum(previous, row) : row;
        }

        var existing = await (
            from fact in _db.DailyFacts
            join entity in _db.Entities on fact.EntityId equals entity.Id
            where entity.AccountId == accountId && fact.Date == date
            select fact).ToListAsync(ct);
        var existingByEntity = existing.ToDictionary(f => f.EntityId);

        foreach (var (entityId, row) in incoming)
        {
            if (!existingByEntity.TryGetValue(entityId, out var fact))
            {
                fact = new AdDailyFact { EntityId = entityId, Date = date };
                _db.DailyFacts.Add(fact);
            }
            fact.Impressions = row.Impressions;
            fact.Clicks = row.Clicks;
            fact.Cost = row.Cost;
            fact.Conversions = row.Conversions;
            fact.ConversionValue = row.ConversionValue;
            fact.Currency = row.Currency;
            fact.SyncedAt = _time.GetUtcNow();
        }

        var stale = existing.Where(f => !incoming.ContainsKey(f.EntityId)).ToList();
        _db.DailyFacts.RemoveRange(stale);

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return new AdUpsertCounts(incoming.Count + stale.Count, unmapped);
    }

    private static AdDailyFactRow Sum(AdDailyFactRow a, AdDailyFactRow b) => a with
    {
        Impressions = a.Impressions + b.Impressions,
        Clicks = a.Clicks + b.Clicks,
        Cost = a.Cost + b.Cost,
        Conversions = a.Conversions + b.Conversions,
        ConversionValue = a.ConversionValue + b.ConversionValue,
    };
}
