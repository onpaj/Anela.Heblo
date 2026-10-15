using System.Reflection;
using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdSettings;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdAccounts;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdPerformance;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdSearchTerms;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetBlendedPerformance;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class MarketingAdsMcpToolsReadTests : MarketingAdsMcpToolsTestBase
{
    [Fact]
    public void ToolSurface_MatchesSpecSection75()
    {
        var names = typeof(MarketingAdsMcpTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => m.Name);

        names.Should().BeEquivalentTo(new[]
        {
            "ListAdAccounts", "GetAdPerformance", "GetAdSearchTerms", "GetBlendedPerformance",
            "GetAdChangeHistory", "GetAdGuardrails", "SubmitAdProposal", "ReviseAdProposal",
            "ListAdProposals", "GetAdProposal", "StartAgentRun", "FinishAgentRun",
            "ListPendingAdProposals", "ApproveAdProposal", "RejectAdProposal",
        });
    }

    [Fact]
    public async Task ListAdAccounts_ForwardsPlatform_AndReturnsJson()
    {
        // Arrange
        Mediator.Setup(m => m.Send(It.IsAny<GetAdAccountsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdAccountsResponse());

        // Act
        var json = await Tools.ListAdAccounts(AdPlatform.Sklik);

        // Assert
        Mediator.Verify(m => m.Send(It.Is<GetAdAccountsRequest>(r => r.Platform == AdPlatform.Sklik), It.IsAny<CancellationToken>()), Times.Once);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetAdPerformance_MapsEveryParameter()
    {
        // Arrange
        Mediator.Setup(m => m.Send(It.IsAny<GetAdPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdPerformanceResponse());

        // Act
        await Tools.GetAdPerformance(AdPlatform.GoogleAds, "123-456-7890", AdEntityLevel.AdGroup,
            "2026-09-01", "2026-09-30", parentExternalId: "cmp-1", pageNumber: 2, pageSize: 25);

        // Assert
        Mediator.Verify(m => m.Send(It.Is<GetAdPerformanceRequest>(r =>
            r.Platform == AdPlatform.GoogleAds &&
            r.AccountExternalId == "123-456-7890" &&
            r.Level == AdEntityLevel.AdGroup &&
            r.From == new DateOnly(2026, 9, 1) &&
            r.To == new DateOnly(2026, 9, 30) &&
            r.ParentExternalId == "cmp-1" &&
            r.PageNumber == 2 &&
            r.PageSize == 25), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("2026-13-01", "2026-09-30")]
    [InlineData("1.9.2026", "2026-09-30")]
    [InlineData("", "2026-09-30")]
    [InlineData("2026-09-30", "2026-09-01")]
    public async Task GetAdPerformance_RejectsBadDates_WithoutCallingTheHandler(string from, string to)
    {
        // Act
        var act = () => Tools.GetAdPerformance(AdPlatform.GoogleAds, "acc", AdEntityLevel.Campaign, from, to);

        // Assert
        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain(nameof(ErrorCodes.ValidationError));
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAdPerformance_RejectsBlankAccount()
    {
        var act = () => Tools.GetAdPerformance(AdPlatform.MetaAds, "  ", AdEntityLevel.Campaign, "2026-09-01", "2026-09-02");

        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain("accountExternalId");
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAdSearchTerms_CampaignParent_MapsToCampaignExternalId()
    {
        // Arrange
        Mediator.Setup(m => m.Send(It.IsAny<GetAdSearchTermsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdSearchTermsResponse());

        // Act
        await Tools.GetAdSearchTerms(AdPlatform.Sklik, "acc", AdEntityLevel.Campaign, "cmp-9", "2026-09-01", "2026-09-07", minCost: 5m, top: 50);

        // Assert
        Mediator.Verify(m => m.Send(It.Is<GetAdSearchTermsRequest>(r =>
            r.Platform == AdPlatform.Sklik &&
            r.AccountExternalId == "acc" &&
            r.CampaignExternalId == "cmp-9" &&
            r.AdGroupExternalId == null &&
            r.From == new DateOnly(2026, 9, 1) &&
            r.To == new DateOnly(2026, 9, 7) &&
            r.MinCost == 5m &&
            r.Top == 50), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAdSearchTerms_AdGroupParent_MapsToAdGroupExternalId()
    {
        Mediator.Setup(m => m.Send(It.IsAny<GetAdSearchTermsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdSearchTermsResponse());

        await Tools.GetAdSearchTerms(AdPlatform.GoogleAds, "acc", AdEntityLevel.AdGroup, "grp-1", "2026-09-01", "2026-09-07");

        Mediator.Verify(m => m.Send(It.Is<GetAdSearchTermsRequest>(r =>
            r.AdGroupExternalId == "grp-1" && r.CampaignExternalId == null && r.Top == 200), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(AdEntityLevel.Keyword)]
    [InlineData(AdEntityLevel.Ad)]
    public async Task GetAdSearchTerms_RejectsParentLevelsOtherThanCampaignOrAdGroup(AdEntityLevel level)
    {
        var act = () => Tools.GetAdSearchTerms(AdPlatform.GoogleAds, "acc", level, "x", "2026-09-01", "2026-09-07");

        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain(nameof(ErrorCodes.ValidationError));
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBlendedPerformance_ForwardsGranularity()
    {
        Mediator.Setup(m => m.Send(It.IsAny<GetBlendedPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetBlendedPerformanceResponse());

        await Tools.GetBlendedPerformance("2026-01-01", "2026-09-30", BlendedGranularity.Daily);

        Mediator.Verify(m => m.Send(It.Is<GetBlendedPerformanceRequest>(r =>
            r.Granularity == BlendedGranularity.Daily &&
            r.From == new DateOnly(2026, 1, 1)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAdChangeHistory_MapsFiltersAndParsesInstants()
    {
        Mediator.Setup(m => m.Send(It.IsAny<GetAdChangeHistoryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdChangeHistoryResponse());

        await Tools.GetAdChangeHistory(AdPlatform.GoogleAds, "acc", since: "2026-09-01T00:00:00Z",
            origin: AdChangeOrigin.OutOfBand, actorKind: AdChangeActorKind.PlatformAutomation);

        Mediator.Verify(m => m.Send(It.Is<GetAdChangeHistoryRequest>(r =>
            r.From == new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) &&
            r.To == null &&
            r.Origin == AdChangeOrigin.OutOfBand &&
            r.ActorKind == AdChangeActorKind.PlatformAutomation), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAdChangeHistory_RejectsMalformedInstant()
    {
        var act = () => Tools.GetAdChangeHistory(since: "yesterday");

        await act.Should().ThrowAsync<McpException>();
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAdGuardrails_SendsSettingsRequest()
    {
        Mediator.Setup(m => m.Send(It.IsAny<GetAdSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdSettingsResponse());

        await Tools.GetAdGuardrails();

        Mediator.Verify(m => m.Send(It.IsAny<GetAdSettingsRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("ListAdAccounts")]
    [InlineData("GetAdPerformance")]
    [InlineData("GetAdSearchTerms")]
    [InlineData("GetBlendedPerformance")]
    [InlineData("GetAdChangeHistory")]
    [InlineData("GetAdGuardrails")]
    public async Task ReadTools_ThrowForbidden_AndSkipMediator_WithoutAdsRead(string tool)
    {
        // Arrange
        DenyRole(AdsRead);

        // Act
        var act = () => tool switch
        {
            "ListAdAccounts" => Tools.ListAdAccounts(),
            "GetAdPerformance" => Tools.GetAdPerformance(AdPlatform.GoogleAds, "acc", AdEntityLevel.Campaign, "2026-09-01", "2026-09-02"),
            "GetAdSearchTerms" => Tools.GetAdSearchTerms(AdPlatform.GoogleAds, "acc", AdEntityLevel.AdGroup, "g", "2026-09-01", "2026-09-02"),
            "GetBlendedPerformance" => Tools.GetBlendedPerformance("2026-09-01", "2026-09-02"),
            "GetAdChangeHistory" => Tools.GetAdChangeHistory(),
            _ => Tools.GetAdGuardrails(),
        };

        // Assert
        var ex = (await act.Should().ThrowAsync<McpException>()).Which;
        ex.Message.Should().Contain("FORBIDDEN").And.Contain(AdsRead);
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task HandlerErrorWithoutParams_BecomesMcpException_NotNullReference()
    {
        // Arrange
        Mediator.Setup(m => m.Send(It.IsAny<GetAdAccountsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdAccountsResponse(ErrorCodes.ResourceNotFound));

        // Act
        var act = () => Tools.ListAdAccounts();

        // Assert
        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().StartWith("[ResourceNotFound]");
    }

    [Fact]
    public async Task HandlerErrorWithParams_IncludesDetail_AndNeverMentionsARace()
    {
        // Arrange
        Mediator.Setup(m => m.Send(It.IsAny<GetAdAccountsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdAccountsResponse(ErrorCodes.ResourceNotFound,
                new Dictionary<string, string> { ["Reason"] = "no such account" }));

        // Act
        var act = () => Tools.ListAdAccounts();

        // Assert
        var message = (await act.Should().ThrowAsync<McpException>()).Which.Message;
        message.Should().StartWith("[ResourceNotFound]").And.Contain("no such account");
        message.Should().NotContainEquivalentOf("same time").And.NotContainEquivalentOf("retry");
    }

    [Fact]
    public async Task ConcurrentChangeError_TellsCallerToReloadAndRetry()
    {
        // Arrange
        Mediator.Setup(m => m.Send(It.IsAny<GetAdAccountsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdAccountsResponse(ErrorCodes.MarketingAdsConcurrentChange));

        // Act
        var act = () => Tools.ListAdAccounts();

        // Assert
        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Be(
            "[MarketingAdsConcurrentChange] Someone changed this at the same time. Reload it (GetAdProposal) and retry.");
    }
}
