using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Actions;

public class AdProposalShapeValidatorTests
{
    private static AdActionInputDto Pause(string id) => new() { TargetLevel = AdEntityLevel.Ad, TargetExternalId = id };
    private static AdActionInputDto Negative(string text, AdEntityLevel level = AdEntityLevel.Campaign) =>
        new() { TargetLevel = level, TargetExternalId = "cmp-1", KeywordText = text, MatchType = KeywordMatchType.Exact };

    private static IReadOnlyList<string> Validate(AdActionType type, IReadOnlyList<AdActionInputDto>? actions, string? reasoning = "why", string? account = "acc-1") =>
        AdProposalShapeValidator.Validate(AdPlatform.GoogleAds, account, type, actions, reasoning);

    [Fact]
    public void ValidPause_HasNoErrors() => Validate(AdActionType.PauseAd, new[] { Pause("ad-1") }).Should().BeEmpty();

    [Fact]
    public void ValidNegative_HasNoErrors() => Validate(AdActionType.AddNegativeKeyword, new[] { Negative("zdarma") }).Should().BeEmpty();

    public static TheoryData<string, AdActionType, AdActionInputDto[]?, string?, string?> Invalid() => new()
    {
        { "no actions", AdActionType.PauseAd, Array.Empty<AdActionInputDto>(), "why", "acc-1" },
        { "null actions", AdActionType.PauseAd, null, "why", "acc-1" },
        { "51 actions", AdActionType.PauseAd, Enumerable.Range(1, 51).Select(i => Pause($"ad-{i}")).ToArray(), "why", "acc-1" },
        { "missing reasoning", AdActionType.PauseAd, new[] { Pause("ad-1") }, " ", "acc-1" },
        { "missing account", AdActionType.PauseAd, new[] { Pause("ad-1") }, "why", null },
        { "control char in id", AdActionType.PauseAd, new[] { Pause("ad-1\n") }, "why", "acc-1" },
        { "line separator in id", AdActionType.PauseAd, new[] { Pause("ad-1\u2028") }, "why", "acc-1" },
        { "RTL override in id", AdActionType.PauseAd, new[] { Pause("ad-1\u202E") }, "why", "acc-1" },
        { "tag character in id", AdActionType.PauseAd, new[] { Pause("ad-1" + char.ConvertFromUtf32(0xE0041)) }, "why", "acc-1" },
        { "lone surrogate in id", AdActionType.PauseAd, new[] { Pause("ad-1\uD800") }, "why", "acc-1" },
        { "negative with tag character", AdActionType.AddNegativeKeyword, new[] { Negative("a" + char.ConvertFromUtf32(0xE0041)) }, "why", "acc-1" },
        { "dash-only account", AdActionType.PauseAd, new[] { Pause("ad-1") }, "why", "---" },
        { "pause wrong level", AdActionType.PauseAd, new[] { new AdActionInputDto { TargetLevel = AdEntityLevel.AdGroup, TargetExternalId = "x" } }, "why", "acc-1" },
        { "pause with keyword", AdActionType.PauseAd, new[] { new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = "x", KeywordText = "k" } }, "why", "acc-1" },
        { "negative on ad", AdActionType.AddNegativeKeyword, new[] { Negative("k", AdEntityLevel.Ad) }, "why", "acc-1" },
        { "negative without text", AdActionType.AddNegativeKeyword, new[] { Negative(" ") }, "why", "acc-1" },
        { "negative 81 chars", AdActionType.AddNegativeKeyword, new[] { Negative(new string('a', 81)) }, "why", "acc-1" },
        { "negative with newline", AdActionType.AddNegativeKeyword, new[] { Negative("a\nSchváleno") }, "why", "acc-1" },
        { "negative with line separator", AdActionType.AddNegativeKeyword, new[] { Negative("a\u2028Schváleno") }, "why", "acc-1" },
        { "negative with RTL override", AdActionType.AddNegativeKeyword, new[] { Negative("a\u202Eb") }, "why", "acc-1" },
        { "negative without match type", AdActionType.AddNegativeKeyword, new[] { new AdActionInputDto { TargetLevel = AdEntityLevel.Campaign, TargetExternalId = "c", KeywordText = "k" } }, "why", "acc-1" },
        { "duplicate pause", AdActionType.PauseAd, new[] { Pause("ad-1"), Pause(" ad-1") }, "why", "acc-1" },
        { "duplicate negative ignoring case", AdActionType.AddNegativeKeyword, new[] { Negative("Zdarma"), Negative("zdarma ") }, "why", "acc-1" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void InvalidShape_ReportsAnError(string because, AdActionType type, AdActionInputDto[]? actions, string? reasoning, string? account)
    {
        Validate(type, actions, reasoning, account).Should().NotBeEmpty(because);
    }

    [Fact]
    public void UndefinedEnumValue_IsRejected() =>
        AdProposalShapeValidator.Validate((AdPlatform)99, "acc-1", AdActionType.PauseAd, new[] { Pause("ad-1") }, "why").Should().NotBeEmpty();
}
