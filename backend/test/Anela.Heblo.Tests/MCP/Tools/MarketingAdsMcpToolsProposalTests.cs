using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.FinishAgentRun;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdProposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SubmitAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class MarketingAdsMcpToolsProposalTests : MarketingAdsMcpToolsTestBase
{
    private const ErrorCodes StaleVersionCode = ErrorCodes.MarketingAdsProposalVersionMismatch;

    private static List<AdActionInputDto> OneNegativeKeyword() => new()
    {
        new AdActionInputDto
        {
            TargetLevel = AdEntityLevel.Campaign,
            TargetExternalId = "cmp-1",
            KeywordText = "zdarma",
            MatchType = KeywordMatchType.Phrase,
        },
    };

    [Fact]
    public async Task SubmitAdProposal_ForwardsEverythingAndTrimsReasoning()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var actions = OneNegativeKeyword();
        Mediator.Setup(m => m.Send(It.IsAny<SubmitAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitAdProposalResponse());

        // Act
        await Tools.SubmitAdProposal(AdPlatform.GoogleAds, " 123-456-7890 ", AdActionType.AddNegativeKeyword,
            actions, "  Wasted spend on free-seekers.  ", runId);

        // Assert
        Mediator.Verify(m => m.Send(It.Is<SubmitAdProposalRequest>(r =>
            r.Platform == AdPlatform.GoogleAds &&
            r.AccountExternalId == "123-456-7890" &&
            r.ActionType == AdActionType.AddNegativeKeyword &&
            r.Actions.SequenceEqual(actions) &&
            r.Reasoning == "Wasted spend on free-seekers." &&
            r.AgentRunId == runId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAdProposal_RejectsEmptyActionList()
    {
        var act = () => Tools.SubmitAdProposal(AdPlatform.GoogleAds, "acc", AdActionType.PauseAd, new List<AdActionInputDto>(), "reason");

        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain(nameof(ErrorCodes.ValidationError));
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAdProposal_RejectsBlankReasoning()
    {
        var act = () => Tools.SubmitAdProposal(AdPlatform.GoogleAds, "acc", AdActionType.AddNegativeKeyword, OneNegativeKeyword(), "   ");

        await act.Should().ThrowAsync<McpException>();
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ReviseAdProposal_ForwardsIdAndExpectedVersion()
    {
        Mediator.Setup(m => m.Send(It.IsAny<ReviseAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReviseAdProposalResponse());

        await Tools.ReviseAdProposal(7, 2, OneNegativeKeyword(), "narrowed to phrase match");

        Mediator.Verify(m => m.Send(It.Is<ReviseAdProposalRequest>(r =>
            r.Id == 7 && r.ExpectedVersion == 2 && r.Actions.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReviseAdProposal_SurfacesStaleVersionAsMcpException()
    {
        Mediator.Setup(m => m.Send(It.IsAny<ReviseAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReviseAdProposalResponse(StaleVersionCode));

        var act = () => Tools.ReviseAdProposal(1, 1, OneNegativeKeyword(), "r");

        var message = (await act.Should().ThrowAsync<McpException>()).Which.Message;
        message.Should().StartWith($"[{StaleVersionCode}]");
    }

    [Fact]
    public async Task ReviseAdProposal_ConcurrentChange_IsMappedToReloadAndRetryHint()
    {
        Mediator.Setup(m => m.Send(It.IsAny<ReviseAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReviseAdProposalResponse(ErrorCodes.MarketingAdsConcurrentChange));

        var act = () => Tools.ReviseAdProposal(1, 1, OneNegativeKeyword(), "r");

        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should()
            .StartWith("[MarketingAdsConcurrentChange]").And.Contain("GetAdProposal");
    }

    [Fact]
    public async Task ListAdProposals_MapsFilters()
    {
        Mediator.Setup(m => m.Send(It.IsAny<ListAdProposalsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListAdProposalsResponse());

        await Tools.ListAdProposals(AdProposalStatus.Rejected, AdPlatform.Sklik, onlyMine: true, pageNumber: 3, pageSize: 10);

        Mediator.Verify(m => m.Send(It.Is<ListAdProposalsRequest>(r =>
            r.Status == AdProposalStatus.Rejected && r.Platform == AdPlatform.Sklik && r.Mine && r.Page == 3 && r.PageSize == 10),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAdProposal_ForwardsId()
    {
        Mediator.Setup(m => m.Send(It.IsAny<GetAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAdProposalResponse());

        await Tools.GetAdProposal(42);

        Mediator.Verify(m => m.Send(It.Is<GetAdProposalRequest>(r => r.Id == 42), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAgentRun_ForwardsTrimmedNameAndScopes()
    {
        Mediator.Setup(m => m.Send(It.IsAny<StartAgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartAgentRunResponse());

        await Tools.StartAgentRun(" paid-search-agent ", new List<string> { "search_terms" });

        Mediator.Verify(m => m.Send(It.Is<StartAgentRunRequest>(r =>
            r.AgentName == "paid-search-agent" && r.DataScopesRead.Single() == "search_terms"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAgentRun_WithoutScopes_SendsEmptyList()
    {
        Mediator.Setup(m => m.Send(It.IsAny<StartAgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartAgentRunResponse());

        await Tools.StartAgentRun("agent");

        Mediator.Verify(m => m.Send(It.Is<StartAgentRunRequest>(r => r.DataScopesRead != null && r.DataScopesRead.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAgentRun_RejectsBlankName()
    {
        var act = () => Tools.StartAgentRun("");

        await act.Should().ThrowAsync<McpException>();
        Mediator.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task FinishAgentRun_ForwardsEveryField()
    {
        var runId = Guid.NewGuid();
        Mediator.Setup(m => m.Send(It.IsAny<FinishAgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinishAgentRunResponse());

        await Tools.FinishAgentRun(runId, AgentRunStatus.Succeeded, "3 proposals", new List<string> { "search_terms" }, 100_000, 20_000, 4.5m);

        Mediator.Verify(m => m.Send(It.Is<FinishAgentRunRequest>(r =>
            r.AgentRunId == runId &&
            r.Status == AgentRunStatus.Succeeded &&
            r.Summary == "3 proposals" &&
            r.DataScopesRead.Single() == "search_terms" &&
            r.InputTokens == 100_000 &&
            r.OutputTokens == 20_000 &&
            r.CostCzk == 4.5m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FinishAgentRun_WithoutScopes_SendsEmptyList()
    {
        Mediator.Setup(m => m.Send(It.IsAny<FinishAgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinishAgentRunResponse());

        await Tools.FinishAgentRun(Guid.NewGuid(), AgentRunStatus.Failed);

        Mediator.Verify(m => m.Send(It.Is<FinishAgentRunRequest>(r => r.DataScopesRead != null && r.DataScopesRead.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("SubmitAdProposal")]
    [InlineData("ReviseAdProposal")]
    [InlineData("StartAgentRun")]
    [InlineData("FinishAgentRun")]
    public async Task WriteTools_ThrowForbidden_WithReadButNotWrite(string tool)
    {
        // Arrange — a read-only marketer
        DenyRole(AdsWrite);

        // Act
        var act = () => tool switch
        {
            "SubmitAdProposal" => Tools.SubmitAdProposal(AdPlatform.GoogleAds, "acc", AdActionType.AddNegativeKeyword, OneNegativeKeyword(), "r"),
            "ReviseAdProposal" => Tools.ReviseAdProposal(1, 1, OneNegativeKeyword(), "r"),
            "StartAgentRun" => Tools.StartAgentRun("agent"),
            _ => Tools.FinishAgentRun(Guid.NewGuid(), AgentRunStatus.Failed),
        };

        // Assert
        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain("FORBIDDEN").And.Contain(AdsWrite);
        Mediator.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ListAdProposals")]
    [InlineData("GetAdProposal")]
    public async Task ProposalReadTools_ThrowForbidden_WithoutAdsRead(string tool)
    {
        DenyRole(AdsRead);

        var act = () => tool == "ListAdProposals" ? Tools.ListAdProposals() : Tools.GetAdProposal(1);

        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain(AdsRead);
        Mediator.Invocations.Should().BeEmpty();
    }
}
