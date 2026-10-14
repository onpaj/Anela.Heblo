using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdProposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RejectAdProposal;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class MarketingAdsMcpToolsApprovalTests : MarketingAdsMcpToolsTestBase
{
    private static AdProposalSummaryDto Summary(int id, int version, DateTime createdAt, params string[] diff) => new()
    {
        Id = id,
        CurrentVersion = version,
        Status = AdProposalStatus.Pending,
        Platform = AdPlatform.GoogleAds,
        AccountExternalId = "123-456-7890",
        ActionType = AdActionType.AddNegativeKeyword,
        ActionCount = diff.Length,
        CreatedByPrincipal = "heblo-marketing-agent",
        CreatedAt = createdAt,
        ExpiresAt = createdAt.AddHours(72),
        DiffLines = diff.ToList(),
    };

    [Fact]
    public async Task ListPendingAdProposals_AsksForPending_AndNumbersOldestFirstWithDiffs()
    {
        // Arrange
        var t0 = new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
        Mediator.Setup(m => m.Send(It.IsAny<ListAdProposalsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListAdProposalsResponse
            {
                Items = new List<AdProposalSummaryDto>
                {
                    Summary(22, 1, t0.AddHours(2), "Pause ad 'Vánoce 2025' in ad group 'Krémy'"),
                    Summary(11, 3, t0, "Add negative \"zdarma\" (Phrase) to campaign 'Brand'", "Add negative \"recept\" (Broad) to campaign 'Brand'"),
                },
                TotalCount = 2,
            });

        // Act
        var json = await Tools.ListPendingAdProposals();

        // Assert
        Mediator.Verify(m => m.Send(It.Is<ListAdProposalsRequest>(r =>
            r.Status == AdProposalStatus.Pending && !r.Mine), It.IsAny<CancellationToken>()), Times.Once);
        using var doc = JsonDocument.Parse(json);
        var pending = doc.RootElement.GetProperty("Pending");
        pending.GetArrayLength().Should().Be(2);
        pending[0].GetProperty("Number").GetInt32().Should().Be(1);
        pending[0].GetProperty("Id").GetInt32().Should().Be(11);
        pending[0].GetProperty("Version").GetInt32().Should().Be(3);
        pending[0].GetProperty("Diff").GetArrayLength().Should().Be(2);
        pending[1].GetProperty("Number").GetInt32().Should().Be(2);
        pending[1].GetProperty("Platform").GetString().Should().Be("GoogleAds");
        doc.RootElement.GetProperty("TotalPending").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ListPendingAdProposals_WithNothingPending_ReturnsEmptyList()
    {
        Mediator.Setup(m => m.Send(It.IsAny<ListAdProposalsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListAdProposalsResponse { Items = new List<AdProposalSummaryDto>(), TotalCount = 0 });

        var json = await Tools.ListPendingAdProposals();

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Pending").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ListPendingAdProposals_AsksTheRepositoryForTheOldestPendingFirst()
    {
        // Arrange
        Mediator.Setup(m => m.Send(It.IsAny<ListAdProposalsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListAdProposalsResponse { Items = new List<AdProposalSummaryDto>(), TotalCount = 0 });

        // Act
        await Tools.ListPendingAdProposals(maxItems: 5);

        // Assert — with more than maxItems pending, the page must hold the oldest, not the newest
        Mediator.Verify(m => m.Send(It.Is<ListAdProposalsRequest>(r => r.OldestFirst), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnexpectedHandlerFailure_IsLoggedAsError_AndSurfacedAsMcpError()
    {
        // Arrange
        Mediator.Setup(m => m.Send(It.IsAny<ApproveAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var act = () => Tools.ApproveAdProposal(1, 1);

        // Assert
        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Be("Failed to approve ad proposal: boom");
        Logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<InvalidOperationException>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task ListPendingAdProposals_ClampsMaxItems()
    {
        Mediator.Setup(m => m.Send(It.IsAny<ListAdProposalsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListAdProposalsResponse { Items = new List<AdProposalSummaryDto>(), TotalCount = 0 });

        await Tools.ListPendingAdProposals(maxItems: 10_000);

        Mediator.Verify(m => m.Send(It.Is<ListAdProposalsRequest>(r => r.PageSize == 50 && r.Page == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAdProposal_ForwardsIdAndVersion()
    {
        Mediator.Setup(m => m.Send(It.IsAny<ApproveAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApproveAdProposalResponse());

        await Tools.ApproveAdProposal(5, 4);

        Mediator.Verify(m => m.Send(It.Is<ApproveAdProposalRequest>(r => r.Id == 5 && r.Version == 4), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAdProposal_MapsHandlerChannelDenialToMcpError()
    {
        // Only the error mapping of a handler denial is tested here (the mediator is mocked). That a
        // service identity is really refused is covered by TokenToApprovalChannelTests.
        Mediator.Setup(m => m.Send(It.IsAny<ApproveAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApproveAdProposalResponse(ErrorCodes.MarketingAdsApprovalChannelDenied,
                new Dictionary<string, string> { ["Reason"] = "Service identities (app-only tokens) can never approve or reject ad proposals." }));

        var act = () => Tools.ApproveAdProposal(1, 1);

        var ex = (await act.Should().ThrowAsync<McpException>()).Which;
        ex.Message.Should().StartWith("[MarketingAdsApprovalChannelDenied]").And.Contain("Service identities");
    }

    [Fact]
    public async Task ApproveAdProposal_ConcurrentChange_IsMappedToReloadAndRetryHint()
    {
        Mediator.Setup(m => m.Send(It.IsAny<ApproveAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApproveAdProposalResponse(ErrorCodes.MarketingAdsConcurrentChange));

        var act = () => Tools.ApproveAdProposal(1, 1);

        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should()
            .StartWith("[MarketingAdsConcurrentChange]").And.Contain("GetAdProposal");
    }

    [Fact]
    public async Task RejectAdProposal_ForwardsTrimmedReason()
    {
        Mediator.Setup(m => m.Send(It.IsAny<RejectAdProposalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RejectAdProposalResponse());

        await Tools.RejectAdProposal(9, 2, "  'recept' converts well  ");

        Mediator.Verify(m => m.Send(It.Is<RejectAdProposalRequest>(r =>
            r.Id == 9 && r.Version == 2 && r.Reason == "'recept' converts well"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectAdProposal_RequiresReason(string reason)
    {
        var act = () => Tools.RejectAdProposal(1, 1, reason);

        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain("reason");
        Mediator.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ListPendingAdProposals")]
    [InlineData("ApproveAdProposal")]
    [InlineData("RejectAdProposal")]
    public async Task ApprovalTools_ThrowForbidden_ForAgentsAndMarketersWithoutApprovalWrite(string tool)
    {
        // Arrange — holds Ads read+write (agent / marketer) but not AdApprovals write
        DenyRole(ApprovalsWrite);

        // Act
        var act = () => tool switch
        {
            "ListPendingAdProposals" => Tools.ListPendingAdProposals(),
            "ApproveAdProposal" => Tools.ApproveAdProposal(1, 1),
            _ => Tools.RejectAdProposal(1, 1, "no"),
        };

        // Assert
        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain("FORBIDDEN").And.Contain(ApprovalsWrite);
        Mediator.Invocations.Should().BeEmpty();
    }
}
