using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Actions;

public class AdActionJsonTests
{
    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var action = AdActionFactory.Create(AdPlatform.Sklik, "acc-1", AdActionType.AddNegativeKeyword,
            new AdActionInputDto { TargetLevel = AdEntityLevel.AdGroup, TargetExternalId = " grp-1 ", KeywordText = " zdarma ", MatchType = KeywordMatchType.Phrase });

        var restored = AdActionJson.Deserialize(AdActionJson.Serialize(new[] { action })).Single();

        restored.Type.Should().Be(AdActionType.AddNegativeKeyword);
        restored.Platform.Should().Be(AdPlatform.Sklik);
        restored.TargetLevel.Should().Be(AdEntityLevel.AdGroup);
        restored.TargetExternalId.Should().Be("grp-1");
        restored.OldValue.Should().Be(AdActionValues.Absent);
        restored.NewValue.Should().Be(AdActionValues.Present);
        restored.Payload[AdActionPayloadKeys.Text].Should().Be("zdarma");
        restored.Payload[AdActionPayloadKeys.MatchType].Should().Be("Phrase");
    }

    [Fact]
    public void Serialize_WritesEnumsByName()
    {
        var action = AdActionFactory.Create(AdPlatform.GoogleAds, "acc-1", AdActionType.PauseAd,
            new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = "ad-1" });

        using var doc = JsonDocument.Parse(AdActionJson.Serialize(new[] { action }));
        var element = doc.RootElement[0];

        element.GetProperty("type").GetString().Should().Be("PauseAd");
        element.GetProperty("platform").GetString().Should().Be("GoogleAds");
        element.GetProperty("oldValue").GetString().Should().Be("Enabled");
        element.GetProperty("newValue").GetString().Should().Be("Paused");
        element.GetProperty("payload").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void Create_GoogleAds_StripsDashesFromAccountId()
    {
        var action = AdActionFactory.Create(AdPlatform.GoogleAds, "123-456-7890", AdActionType.PauseAd,
            new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = "ad-1" });

        action.AccountExternalId.Should().Be("1234567890");
    }

    [Fact]
    public void Create_NonGoogle_KeepsAccountIdVerbatim()
    {
        var action = AdActionFactory.Create(AdPlatform.Sklik, "12-34", AdActionType.PauseAd,
            new AdActionInputDto { TargetLevel = AdEntityLevel.Ad, TargetExternalId = "ad-1" });

        action.AccountExternalId.Should().Be("12-34");
    }
}
