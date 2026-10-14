using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Domain;

public class AdProposalTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(72);

    private static AdProposal NewProposal(int actionCount = 2) => AdProposal.Create(
        new AdProposalDraft("GoogleAds", "acc-1", "PauseAd", "agent-1", AdActorKind.Agent, null, "[]", actionCount, "why"),
        Now, Ttl);

    [Fact]
    public void Create_StartsPendingAtVersionOneWithTtl()
    {
        var p = NewProposal();

        p.Status.Should().Be(AdProposalStatus.Pending);
        p.CurrentVersion.Should().Be(1);
        p.Versions.Should().ContainSingle(v => v.Version == 1 && v.Reasoning == "why" && v.CreatedBy == "agent-1");
        p.ExpiresAt.Should().Be(Now + Ttl);
        p.Revision.Should().Be(1);
    }

    [Fact]
    public void Revise_AddsANewVersionAndVoidsEarlierApprovals()
    {
        var p = NewProposal();
        p.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: false);

        p.Revise("[]", 1, "better", "agent-1", Now.AddHours(1), Ttl);

        p.CurrentVersion.Should().Be(2);
        p.Versions.Select(v => v.Version).Should().BeEquivalentTo(new[] { 1, 2 });
        p.GetCurrentApprovers().Should().BeEmpty("approvals of version 1 do not carry over");
        p.ExpiresAt.Should().Be(Now.AddHours(1) + Ttl);
        p.Status.Should().Be(AdProposalStatus.Pending);
    }

    [Fact]
    public void RecordApproval_NotFinal_StaysPending_Final_Approves()
    {
        var p = NewProposal();

        p.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: false);
        p.Status.Should().Be(AdProposalStatus.Pending);

        p.RecordApproval("specialist", ApprovalChannel.Web, Now, isFinal: true);
        p.Status.Should().Be(AdProposalStatus.Approved);
        p.GetCurrentApprovers().Should().BeEquivalentTo(new[] { "chief", "specialist" });
    }

    [Fact]
    public void Reject_StoresReasonAndMovesToRejected()
    {
        var p = NewProposal();
        p.Reject("chief", ApprovalChannel.Mcp, "too broad", Now);

        p.Status.Should().Be(AdProposalStatus.Rejected);
        p.Approvals.Should().ContainSingle(a => a.Decision == ApprovalDecision.Rejected && a.Reason == "too broad" && a.Channel == ApprovalChannel.Mcp);
    }

    [Fact]
    public void Revise_WhenNotPending_Throws()
    {
        var p = NewProposal();
        p.Reject("chief", ApprovalChannel.Web, "no", Now);

        var act = () => p.Revise("[]", 1, "x", "agent-1", Now, Ttl);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(2, 2, AdProposalStatus.Executed)]
    [InlineData(2, 1, AdProposalStatus.PartiallyExecuted)]
    [InlineData(2, 0, AdProposalStatus.Failed)]
    public void FinishExecution_DerivesStatusFromSucceededActions(int actions, int succeeded, AdProposalStatus expected)
    {
        var p = NewProposal(actions);
        p.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: true);
        p.StartExecution(Now);
        for (var i = 0; i < actions; i++)
        {
            p.RecordExecution(new AdProposalActionExecution
            {
                Version = 1,
                ActionIndex = i,
                State = i < succeeded ? AdActionExecutionState.Succeeded : AdActionExecutionState.Failed,
            });
        }

        p.FinishExecution(Now.AddMinutes(1));

        p.Status.Should().Be(expected);
        p.ExecutionFinishedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void IsExpiredAt_OnlyForPendingPastExpiry()
    {
        var p = NewProposal();

        p.IsExpiredAt(Now + Ttl - TimeSpan.FromSeconds(1)).Should().BeFalse();
        p.IsExpiredAt(Now + Ttl).Should().BeTrue();

        p.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: true);
        p.IsExpiredAt(Now + Ttl).Should().BeFalse("an approved proposal no longer expires");
    }

    [Fact]
    public void EveryStateChange_BumpsRevision()
    {
        var p = NewProposal();
        var before = p.Revision;

        p.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: false);

        p.Revision.Should().Be(before + 1);
    }
}
