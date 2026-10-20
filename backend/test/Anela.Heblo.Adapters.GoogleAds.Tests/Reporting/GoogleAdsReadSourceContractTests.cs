using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsReadSourceContractTests : AdPlatformReadSourceContractTests
{
    protected override IAdPlatformReadSource CreateSource() =>
        ReadSourceHarness.Create(new FixtureGoogleAdsApiClient()).Source;

    protected override string AccountExternalId => TestSettings.CustomerId;

    protected override DateOnly FixtureDate => ReadSourceHarness.FixtureDate;
}
