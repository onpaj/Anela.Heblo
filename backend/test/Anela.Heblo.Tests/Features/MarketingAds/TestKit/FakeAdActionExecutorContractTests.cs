using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class FakeAdActionExecutorContractTests : AdActionExecutorContractTests
{
    private const string Account = "123-456-7890";

    protected override IAdActionExecutor CreateExecutor() =>
        new FakeAdActionExecutor(AdPlatform.GoogleAds)
            .SeedAd(Account, "ad-1")
            .SeedNegativeKeywordTarget(Account, AdEntityLevel.AdGroup, "adgroup-1");

    protected override AdAction SamplePauseAd() =>
        new(AdActionType.PauseAd, AdPlatform.GoogleAds, Account, AdEntityLevel.Ad, "ad-1",
            AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

    protected override AdAction? SampleAddNegativeKeyword() =>
        new(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, Account, AdEntityLevel.AdGroup, "adgroup-1",
            AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string>
            {
                [AdActionPayloadKeys.Text] = "zdarma",
                [AdActionPayloadKeys.MatchType] = nameof(KeywordMatchType.Phrase),
            });
}

/// <summary>Meta-like: PauseAd only, no negative keywords.</summary>
public class FakeAdActionExecutorPauseOnlyContractTests : AdActionExecutorContractTests
{
    private const string Account = "act_1";

    protected override IAdActionExecutor CreateExecutor() =>
        new FakeAdActionExecutor(AdPlatform.MetaAds, AdActionType.PauseAd).SeedAd(Account, "ad-9");

    protected override AdAction SamplePauseAd() =>
        new(AdActionType.PauseAd, AdPlatform.MetaAds, Account, AdEntityLevel.Ad, "ad-9",
            AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

    protected override AdAction? SampleAddNegativeKeyword() => null;
}
