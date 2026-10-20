using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds;

public class AdContractEnumTests
{
    [Fact]
    public void Enum_values_match_the_canonical_names_in_spec_section_12_2()
    {
        ((int)AdPlatform.GoogleAds).Should().Be(1);
        ((int)AdPlatform.MetaAds).Should().Be(2);
        ((int)AdPlatform.Sklik).Should().Be(3);

        ((int)AdEntityLevel.Campaign).Should().Be(1);
        ((int)AdEntityLevel.AdGroup).Should().Be(2);
        ((int)AdEntityLevel.Keyword).Should().Be(3);
        ((int)AdEntityLevel.NegativeKeyword).Should().Be(4);
        ((int)AdEntityLevel.Ad).Should().Be(5);

        ((int)AdEntityStatus.Unknown).Should().Be(0);
        ((int)AdEntityStatus.Enabled).Should().Be(1);
        ((int)AdEntityStatus.Paused).Should().Be(2);
        ((int)AdEntityStatus.Removed).Should().Be(3);

        ((int)KeywordMatchType.Exact).Should().Be(1);
        ((int)KeywordMatchType.Phrase).Should().Be(2);
        ((int)KeywordMatchType.Broad).Should().Be(3);

        ((int)AdChangeActorKind.Unknown).Should().Be(0);
        ((int)AdChangeActorKind.Heblo).Should().Be(1);
        ((int)AdChangeActorKind.User).Should().Be(2);
        ((int)AdChangeActorKind.PlatformAutomation).Should().Be(3);

        ((int)AdActionType.AddNegativeKeyword).Should().Be(1);
        ((int)AdActionType.PauseAd).Should().Be(2);

        ((int)AdExecutionOutcome.Succeeded).Should().Be(1);
        ((int)AdExecutionOutcome.Failed).Should().Be(2);
        ((int)AdExecutionOutcome.StaleState).Should().Be(3);
    }

    [Fact]
    public void Action_value_and_payload_key_constants_match_the_spec()
    {
        AdActionValues.Absent.Should().Be("Absent");
        AdActionValues.Present.Should().Be("Present");
        AdActionValues.Enabled.Should().Be("Enabled");
        AdActionValues.Paused.Should().Be("Paused");
        AdActionPayloadKeys.Text.Should().Be("text");
        AdActionPayloadKeys.MatchType.Should().Be("matchType");
    }
}
