using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads.Entities;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdsDbValuesTests
{
    [Theory]
    [InlineData(AdPlatform.GoogleAds, "GoogleAds")]
    [InlineData(AdPlatform.MetaAds, "MetaAds")]
    [InlineData(AdPlatform.Sklik, "Sklik")]
    public void Platform_round_trips_through_its_member_name(AdPlatform platform, string column)
    {
        // Act + Assert
        AdsDbValues.ToDb(platform).Should().Be(column);
        AdsDbValues.FromDb<AdPlatform>(column).Should().Be(platform);
    }

    [Fact]
    public void Unknown_column_value_throws_with_the_value_in_the_message()
    {
        // Act
        var act = () => AdsDbValues.FromDb<AdEntityLevel>("Banana");

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Banana*");
    }

    [Fact]
    public void Missing_match_type_is_stored_as_Unknown_and_read_back_as_null()
    {
        AdsDbValues.MatchTypeToDb(null).Should().Be(AdsDbValues.NoMatchType);
        AdsDbValues.MatchTypeFromDb(AdsDbValues.NoMatchType).Should().BeNull();
        AdsDbValues.MatchTypeToDb(KeywordMatchType.Phrase).Should().Be("Phrase");
        AdsDbValues.MatchTypeFromDb("Exact").Should().Be(KeywordMatchType.Exact);
    }

    [Fact]
    public void C2_enums_store_exactly_C1s_vocabulary()
    {
        // Act + Assert
        Enum.GetValues<AdChangeOrigin>().Select(AdsDbValues.ToDb)
            .Should().BeEquivalentTo(new[] { AdChangeOrigins.Heblo, AdChangeOrigins.OutOfBand });
        Enum.GetValues<AdChangeSource>().Select(AdsDbValues.ToDb)
            .Should().BeEquivalentTo(new[] { AdChangeSources.PlatformChangeLog, AdChangeSources.SnapshotDiff });
    }
}
