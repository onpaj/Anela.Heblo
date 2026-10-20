using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

public sealed class GoogleAdsActionExecutorContractTests : AdActionExecutorContractTests
{
    protected override IAdActionExecutor CreateExecutor() => ExecutorHarness.Create(ExecutorHarness.SeededApi());

    protected override AdAction SamplePauseAd() => ExecutorHarness.PauseAd("221~441");

    protected override AdAction? SampleAddNegativeKeyword() =>
        ExecutorHarness.AddNegative(AdEntityLevel.Campaign, "111", "zdarma", nameof(KeywordMatchType.Exact));
}
