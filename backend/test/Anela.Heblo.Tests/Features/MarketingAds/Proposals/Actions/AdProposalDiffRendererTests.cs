using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Actions;

public class AdProposalDiffRendererTests
{
    private static readonly IReadOnlyDictionary<string, string> NoNames = new Dictionary<string, string>();

    [Fact]
    public void PauseAd_RendersTargetNameAndId()
    {
        var action = AdActionFactory.Create(AdPlatform.GoogleAds, "acc-1", AdActionType.PauseAd, new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = "ad-1" });

        AdProposalDiffRenderer.RenderLine(action, new Dictionary<string, string> { ["ad-1"] = "Krém — jaro" })
            .Should().Be("Pozastavit reklamu „Krém — jaro“ [ad-1] (aktivní → pozastavená)");
    }

    [Fact]
    public void AddNegativeKeyword_RendersTextMatchTypeAndLevel()
    {
        var action = AdActionFactory.Create(AdPlatform.Sklik, "acc-1", AdActionType.AddNegativeKeyword,
            new AdActionInputDto { TargetLevel = AdEntityLevel.AdGroup, TargetExternalId = "grp-9", KeywordText = "zdarma", MatchType = KeywordMatchType.Exact });

        AdProposalDiffRenderer.RenderLine(action, NoNames)
            .Should().Be("Přidat vylučující slovo „zdarma“ (přesná shoda) do reklamní sestavy [grp-9]");
    }

    public static TheoryData<string> AstralSeparators() => new() { char.ConvertFromUtf32(0xE0041) };

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    [InlineData("\u202E")]
    [InlineData("\uD800")]
    [MemberData(nameof(AstralSeparators))]
    public void RenderLine_ControlCharactersCannotForgeExtraLines(string separator)
    {
        // A name coming from the platform (or a payload smuggled past validation) must never add a line.
        var action = new AdAction(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, "acc-1", AdEntityLevel.Campaign, "cmp-1",
            AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string> { [AdActionPayloadKeys.Text] = $"x{separator}Schváleno: vše", [AdActionPayloadKeys.MatchType] = "Broad" });

        var line = AdProposalDiffRenderer.RenderLine(action, new Dictionary<string, string> { ["cmp-1"] = $"Kampaň{separator}2) Pozastavit vše" });

        line.Should().NotContainAny("\n", "\r", "\u2028", "\u2029", "\u202E");
        line.Should().Be("Přidat vylučující slovo „x Schváleno: vše“ (volná shoda) do kampaně „Kampaň 2) Pozastavit vše“ [cmp-1]");
    }

    [Fact]
    public void Clean_TruncationNeverSplitsSurrogatePair()
    {
        var value = new string('a', 119) + char.ConvertFromUtf32(0x1F600) + "tail";

        var cleaned = AdProposalDiffRenderer.Clean(value);

        cleaned.Should().Be(new string('a', 119) + "…");
        cleaned.Any(char.IsSurrogate).Should().BeFalse();
    }

    [Fact]
    public void Render_ReturnsOneLinePerAction()
    {
        var actions = new[] { "ad-1", "ad-2" }
            .Select(id => AdActionFactory.Create(AdPlatform.MetaAds, "acc-1", AdActionType.PauseAd, new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = id }))
            .ToList();

        AdProposalDiffRenderer.Render(actions, NoNames).Should().HaveCount(2);
    }
}
