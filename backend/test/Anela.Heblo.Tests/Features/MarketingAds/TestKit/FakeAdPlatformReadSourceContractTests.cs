using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class FakeAdPlatformReadSourceContractTests : AdPlatformReadSourceContractTests
{
    private const string Account = "123-456-7890";
    private static readonly DateOnly Date = new(2026, 10, 6);

    protected override IAdPlatformReadSource CreateSource() =>
        FakeAdPlatformReadSource.CreateSample(AdPlatform.GoogleAds, Account, Date);

    protected override string AccountExternalId => Account;
    protected override DateOnly FixtureDate => Date;
}

public class FakeAdPlatformReadSourceWithoutOptionalCapabilitiesContractTests : AdPlatformReadSourceContractTests
{
    private const string Account = "act_1";
    private static readonly DateOnly Date = new(2026, 10, 6);

    protected override IAdPlatformReadSource CreateSource() =>
        FakeAdPlatformReadSource.CreateSample(
            AdPlatform.MetaAds, Account, Date,
            new AdSourceCapabilities(SearchTerms: false, ChangeLog: false, ChangeLogMaxAge: null));

    protected override string AccountExternalId => Account;
    protected override DateOnly FixtureDate => Date;
}
