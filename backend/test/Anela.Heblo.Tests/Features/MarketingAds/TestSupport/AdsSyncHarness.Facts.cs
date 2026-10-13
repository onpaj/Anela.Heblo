using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

internal sealed partial class AdsSyncHarness
{
    public AdDailyFactUpserter Facts => new(Db, Time);
    public AdSearchTermUpserter SearchTerms => new(Db, Time);

    /// <summary>Creates a managed account with campaign c-1 > ad group g-1 > ad ad-1 and returns its id and entity ids.</summary>
    public async Task<(long AccountId, IReadOnlyDictionary<AdEntityKey, long> Ids)> SeedTreeAsync(string accountExternalId = AdsTestData.AccountId)
    {
        var account = (await Accounts.UpsertAsync(AdPlatform.GoogleAds, new[] { AdsTestData.Account(accountExternalId) }, default)).Single();
        var result = await Entities.UpsertAsync(account.Id,
            new[] { AdsTestData.Campaign("c-1"), AdsTestData.AdGroup("g-1", "c-1"), AdsTestData.Ad("ad-1", "g-1") }, false, default);
        return (account.Id, result.EntityIds);
    }
}
