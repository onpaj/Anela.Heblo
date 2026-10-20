using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;

/// <summary>Reads the C1 <c>ads</c> schema through <see cref="AdsDbContext"/>. Enum columns hold the enum names.</summary>
public sealed class AdsDbReadModel : IAdsReadModel
{
    private readonly AdsDbContext _db;

    public AdsDbReadModel(AdsDbContext db) => _db = db;

    public async Task<bool?> IsAccountManagedAsync(AdPlatform platform, string accountExternalId, CancellationToken cancellationToken)
    {
        var platformName = platform.ToString();
        var managed = await _db.Accounts.AsNoTracking()
            .Where(a => a.Platform == platformName && a.ExternalId == accountExternalId)
            .Select(a => (bool?)a.IsManaged)
            .FirstOrDefaultAsync(cancellationToken);
        return managed;
    }

    public async Task<IReadOnlyDictionary<string, AdGroupOccupancy>> GetAdGroupOccupancyAsync(AdPlatform platform,
        string accountExternalId, IReadOnlyCollection<string> adExternalIds, CancellationToken cancellationToken)
    {
        if (adExternalIds.Count == 0)
        {
            return new Dictionary<string, AdGroupOccupancy>();
        }

        var platformName = platform.ToString();
        var adLevel = nameof(AdEntityLevel.Ad);
        var enabled = nameof(AdEntityStatus.Enabled);
        var ids = adExternalIds.ToArray();

        var rows = await (
            from ad in _db.Entities.AsNoTracking()
            join acc in _db.Accounts.AsNoTracking() on ad.AccountId equals acc.Id
            join grp in _db.Entities.AsNoTracking() on ad.ParentId equals (long?)grp.Id
            where acc.Platform == platformName && acc.ExternalId == accountExternalId
                  && ad.Level == adLevel && ids.Contains(ad.ExternalId)
            select new
            {
                AdExternalId = ad.ExternalId,
                AdGroupExternalId = grp.ExternalId,
                EnabledAdCount = _db.Entities.Count(s => s.ParentId == grp.Id && s.Level == adLevel && s.Status == enabled),
            }).ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.AdExternalId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new AdGroupOccupancy(g.First().AdGroupExternalId, g.First().EnabledAdCount), StringComparer.Ordinal);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetEntityNamesAsync(AdPlatform platform, string accountExternalId,
        IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken)
    {
        if (externalIds.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var platformName = platform.ToString();
        var ids = externalIds.ToArray();
        var rows = await (
            from e in _db.Entities.AsNoTracking()
            join acc in _db.Accounts.AsNoTracking() on e.AccountId equals acc.Id
            where acc.Platform == platformName && acc.ExternalId == accountExternalId && ids.Contains(e.ExternalId)
            select new { e.ExternalId, e.Name }).ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.ExternalId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
    }
}
