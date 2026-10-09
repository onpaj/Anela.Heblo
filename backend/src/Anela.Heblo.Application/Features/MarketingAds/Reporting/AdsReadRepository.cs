using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

/// <summary>
/// Read side for agents (ADR-008: operational, not reporting). Every fact query aggregates
/// exactly one entity level — facts are stored at every level and summing levels double-counts.
/// </summary>
public sealed class AdsReadRepository : IAdsReadRepository
{
    private const string UndefinedTable = "42P01";
    private readonly AdsDbContext _db;

    public AdsReadRepository(AdsDbContext db) => _db = db;

    public bool IsAvailable => true;

    public async Task<IReadOnlyList<AdAccountDto>> ListAccountsAsync(AdPlatform? platform, bool includeUnmanaged, CancellationToken ct)
    {
        var query = _db.Accounts.AsNoTracking();
        if (platform is { } p)
        {
            var value = AdsDbValues.ToDb(p);
            query = query.Where(a => a.Platform == value);
        }
        if (!includeUnmanaged)
            query = query.Where(a => a.IsManaged);

        var rows = await query.OrderBy(a => a.Platform).ThenBy(a => a.Name).ToListAsync(ct);
        return rows.Select(a => new AdAccountDto
        {
            Platform = AdsDbValues.FromDb<AdPlatform>(a.Platform),
            ExternalId = a.ExternalId,
            Name = a.Name,
            Currency = a.Currency,
            TimeZone = a.TimeZone,
            IsManaged = a.IsManaged,
        }).ToList();
    }

    public async Task<AdPerformancePage?> GetPerformanceAsync(AdPerformanceQuery query, CancellationToken ct)
    {
        var account = await FindAccountAsync(query.Platform, query.AccountExternalId, ct);
        if (account is null)
            return null;

        var level = AdsDbValues.ToDb(query.Level);
        var entities = _db.Entities.AsNoTracking().Where(e => e.AccountId == account.Value.Id && e.Level == level);
        if (!string.IsNullOrWhiteSpace(query.ParentExternalId))
        {
            var parentIds = _db.Entities.Where(e => e.AccountId == account.Value.Id && e.Level != level && e.ExternalId == query.ParentExternalId)
                .Select(e => (long?)e.Id);
            entities = entities.Where(e => parentIds.Contains(e.ParentId));
        }

        var grouped =
            from fact in _db.DailyFacts.AsNoTracking()
            join entity in entities on fact.EntityId equals entity.Id
            where fact.Date >= query.From && fact.Date <= query.To
            group fact by fact.EntityId into g
            select new
            {
                EntityId = g.Key,
                Impressions = g.Sum(x => x.Impressions),
                Clicks = g.Sum(x => x.Clicks),
                Cost = g.Sum(x => x.Cost),
                Conversions = g.Sum(x => x.Conversions),
                ConversionValue = g.Sum(x => x.ConversionValue),
            };

        var total = await grouped.CountAsync(ct);
        var page = await grouped.OrderByDescending(x => x.Cost).ThenBy(x => x.EntityId)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var names = await LoadNamesAsync(page.Select(x => x.EntityId), ct);

        var rows = page.Select(x =>
        {
            var entity = names[x.EntityId];
            var parent = entity.ParentId is { } pid && names.TryGetValue(pid, out var p) ? p : null;
            return new AdPerformanceRowDto
            {
                EntityExternalId = entity.ExternalId,
                Name = entity.Name,
                Status = AdsDbValues.FromDb<AdEntityStatus>(entity.Status),
                ParentExternalId = parent?.ExternalId,
                ParentName = parent?.Name,
                Impressions = x.Impressions,
                Clicks = x.Clicks,
                Cost = x.Cost,
                Conversions = x.Conversions,
                ConversionValue = x.ConversionValue,
                CtrPercent = AdMetrics.CtrPercent(x.Impressions, x.Clicks),
                AverageCpc = AdMetrics.AverageCpc(x.Cost, x.Clicks),
                Roas = AdMetrics.Roas(x.ConversionValue, x.Cost),
            };
        }).ToList();

        return new AdPerformancePage(account.Value.Currency, total, rows);
    }

    public async Task<IReadOnlyList<AdSearchTermRowDto>?> GetSearchTermsAsync(AdSearchTermQuery query, CancellationToken ct)
    {
        var account = await FindAccountAsync(query.Platform, query.AccountExternalId, ct);
        if (account is null)
            return null;

        var adGroupLevel = AdsDbValues.ToDb(AdEntityLevel.AdGroup);
        var campaignLevel = AdsDbValues.ToDb(AdEntityLevel.Campaign);
        var adGroups = _db.Entities.AsNoTracking().Where(e => e.AccountId == account.Value.Id && e.Level == adGroupLevel);
        if (!string.IsNullOrWhiteSpace(query.AdGroupExternalId))
            adGroups = adGroups.Where(e => e.ExternalId == query.AdGroupExternalId);
        if (!string.IsNullOrWhiteSpace(query.CampaignExternalId))
        {
            var campaignIds = _db.Entities.Where(e => e.AccountId == account.Value.Id && e.Level == campaignLevel && e.ExternalId == query.CampaignExternalId)
                .Select(e => (long?)e.Id);
            adGroups = adGroups.Where(e => campaignIds.Contains(e.ParentId));
        }

        var grouped =
            from term in _db.SearchTermsDaily.AsNoTracking()
            join adGroup in adGroups on term.AdGroupEntityId equals adGroup.Id
            where term.Date >= query.From && term.Date <= query.To
            group term by new { term.AdGroupEntityId, term.SearchTerm, term.MatchType } into g
            select new
            {
                g.Key.AdGroupEntityId,
                g.Key.SearchTerm,
                g.Key.MatchType,
                Impressions = g.Sum(x => x.Impressions),
                Clicks = g.Sum(x => x.Clicks),
                Cost = g.Sum(x => x.Cost),
                Conversions = g.Sum(x => x.Conversions),
                ConversionValue = g.Sum(x => x.ConversionValue),
            };
        if (query.MinCost is { } minCost)
            grouped = grouped.Where(x => x.Cost >= minCost);

        var rows = await grouped.OrderByDescending(x => x.Cost).ThenBy(x => x.SearchTerm).Take(query.Top).ToListAsync(ct);
        var adGroupNames = await LoadNamesAsync(rows.Select(r => r.AdGroupEntityId), ct);
        var campaignNames = await LoadNamesAsync(adGroupNames.Values.Where(a => a.ParentId is not null).Select(a => a.ParentId!.Value), ct);

        return rows.Select(r =>
        {
            var adGroup = adGroupNames[r.AdGroupEntityId];
            var campaign = adGroup.ParentId is { } pid && campaignNames.TryGetValue(pid, out var c) ? c : null;
            return new AdSearchTermRowDto
            {
                SearchTerm = r.SearchTerm,
                MatchType = AdsDbValues.MatchTypeFromDb(r.MatchType),
                AdGroupExternalId = adGroup.ExternalId,
                AdGroupName = adGroup.Name,
                CampaignExternalId = campaign?.ExternalId,
                CampaignName = campaign?.Name,
                Impressions = r.Impressions,
                Clicks = r.Clicks,
                Cost = r.Cost,
                Conversions = r.Conversions,
                ConversionValue = r.ConversionValue,
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<AdBlendedDayRow>?> GetBlendedDailyAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        try
        {
            return await _db.Database.SqlQuery<AdBlendedDayRow>($@"
                SELECT date AS ""Date"", ad_cost_czk AS ""AdCostCzk"", google_ads_cost_czk AS ""GoogleAdsCostCzk"",
                       meta_ads_cost_czk AS ""MetaAdsCostCzk"", sklik_cost_czk AS ""SklikCostCzk"",
                       has_non_czk_ad_cost AS ""HasNonCzkAdCost"", orders::int AS ""Orders"",
                       revenue_without_vat_czk AS ""RevenueWithoutVatCzk"", eshop_revenue_without_vat_czk AS ""EshopRevenueWithoutVatCzk""
                FROM ads.v_ads_blended_daily
                WHERE date >= {from} AND date <= {to}
                ORDER BY date").ToListAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == UndefinedTable)
        {
            return null;
        }
    }

    public async Task<AdChangeEventPage> GetChangeEventsAsync(AdChangeEventQuery query, CancellationToken ct)
    {
        var fromUtc = query.From.ToUniversalTime();
        var toUtc = query.To.ToUniversalTime();
        var rows =
            from change in _db.ChangeEvents.AsNoTracking()
            join account in _db.Accounts.AsNoTracking() on change.AccountId equals account.Id
            where change.OccurredAt >= fromUtc && change.OccurredAt <= toUtc
            select new { change, account };

        if (query.Platform is { } platform)
        {
            var value = AdsDbValues.ToDb(platform);
            rows = rows.Where(x => x.account.Platform == value);
        }
        if (!string.IsNullOrWhiteSpace(query.AccountExternalId))
            rows = rows.Where(x => x.account.ExternalId == query.AccountExternalId);
        if (query.Origin is { } origin)
        {
            var value = AdsDbValues.ToDb(origin);
            rows = rows.Where(x => x.change.Origin == value);
        }
        if (query.ActorKind is { } actorKind)
        {
            var value = AdsDbValues.ToDb(actorKind);
            rows = rows.Where(x => x.change.ActorKind == value);
        }

        var total = await rows.CountAsync(ct);
        var page = await rows.OrderByDescending(x => x.change.OccurredAt).ThenByDescending(x => x.change.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var entities = await LoadNamesAsync(page.Where(x => x.change.EntityId is not null).Select(x => x.change.EntityId!.Value), ct);

        return new AdChangeEventPage(total, page.Select(x =>
        {
            var entity = x.change.EntityId is { } id && entities.TryGetValue(id, out var e) ? e : null;
            return new AdChangeEventDto
            {
                Id = x.change.Id,
                Platform = AdsDbValues.FromDb<AdPlatform>(x.account.Platform),
                AccountExternalId = x.account.ExternalId,
                OccurredAt = x.change.OccurredAt,
                Actor = x.change.Actor,
                ActorKind = AdsDbValues.FromDb<AdChangeActorKind>(x.change.ActorKind),
                EntityLevel = entity is not null ? AdsDbValues.FromDb<AdEntityLevel>(entity.Level)
                    : x.change.EntityLevel is { } level ? AdsDbValues.FromDb<AdEntityLevel>(level) : null,
                EntityExternalId = entity?.ExternalId ?? x.change.EntityExternalRef,
                EntityName = entity?.Name,
                ChangeType = x.change.ChangeType,
                OldValueJson = x.change.OldValueJson,
                NewValueJson = x.change.NewValueJson,
                Source = AdsDbValues.FromDb<AdChangeSource>(x.change.Source),
                Origin = AdsDbValues.FromDb<AdChangeOrigin>(x.change.Origin),
                MatchedExecutionId = x.change.MatchedExecutionId,
            };
        }).ToList());
    }

    private async Task<(long Id, string Currency)?> FindAccountAsync(AdPlatform platform, string externalId, CancellationToken ct)
    {
        var value = AdsDbValues.ToDb(platform);
        var account = await _db.Accounts.AsNoTracking()
            .Where(a => a.Platform == value && a.ExternalId == externalId)
            .Select(a => new { a.Id, a.Currency })
            .FirstOrDefaultAsync(ct);
        return account is null ? null : (account.Id, account.Currency);
    }

    private sealed record EntityName(long Id, string Level, string ExternalId, string Name, string Status, long? ParentId);

    /// <summary>Loads the given entities and their parents.</summary>
    private async Task<IReadOnlyDictionary<long, EntityName>> LoadNamesAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
            return new Dictionary<long, EntityName>();

        var rows = await _db.Entities.AsNoTracking()
            .Where(e => wanted.Contains(e.Id))
            .Select(e => new EntityName(e.Id, e.Level, e.ExternalId, e.Name, e.Status, e.ParentId))
            .ToListAsync(ct);
        var parentIds = rows.Where(r => r.ParentId is not null).Select(r => r.ParentId!.Value).Except(wanted).Distinct().ToList();
        var parents = parentIds.Count == 0 ? new List<EntityName>() : await _db.Entities.AsNoTracking()
            .Where(e => parentIds.Contains(e.Id))
            .Select(e => new EntityName(e.Id, e.Level, e.ExternalId, e.Name, e.Status, e.ParentId))
            .ToListAsync(ct);
        return rows.Concat(parents).ToDictionary(r => r.Id);
    }
}
