using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals;

public class AdProposalDtoMapperTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlyDictionary<string, string> NoNames = new Dictionary<string, string>();

    private static AdProposal RevisedProposal()
    {
        var v1 = new[] { ProposalTestHarness.PauseAction("ad-1"), ProposalTestHarness.PauseAction("ad-2") };
        var proposal = AdProposal.Create(new AdProposalDraft("GoogleAds", "acc-1", "PauseAd", "agent-1", AdActorKind.Agent,
            null, AdActionJson.Serialize(v1), v1.Length, "first"), Now, TimeSpan.FromHours(72));
        proposal.Id = 7;
        proposal.RecordApproval("chief", ApprovalChannel.Web, Now.AddMinutes(5), isFinal: false);
        var v2 = new[] { ProposalTestHarness.PauseAction("ad-2") };
        proposal.Revise(AdActionJson.Serialize(v2), v2.Length, "second", "agent-1", Now.AddHours(1), TimeSpan.FromHours(72));
        return proposal;
    }

    [Fact]
    public void ToDto_CopiesDecisionAndReason()
    {
        var dto = AdProposalDtoMapper.ToDto(AdLimitsEvaluation.RequireWebApproval("why"));

        dto.Decision.Should().Be(AdLimitsDecision.NeedsWebApproval);
        dto.Reason.Should().Be("why");
    }

    [Fact]
    public void ToSummary_DescribesTheCurrentVersionWithResolvedNames()
    {
        var summary = AdProposalDtoMapper.ToSummary(RevisedProposal(), new Dictionary<string, string> { ["ad-2"] = "Summer sale" });

        summary.Id.Should().Be(7);
        summary.CurrentVersion.Should().Be(2);
        summary.Platform.Should().Be(AdPlatform.GoogleAds);
        summary.ActionType.Should().Be(AdActionType.PauseAd);
        summary.ActionCount.Should().Be(1);
        summary.ExpiresAt.Should().Be(Now.AddHours(73));
        summary.DiffLines.Should().ContainSingle().Which.Should().Contain("Summer sale");
    }

    [Fact]
    public void ToDetail_ListsVersionsNewestFirstAndApprovalsInOrder()
    {
        var detail = AdProposalDtoMapper.ToDetail(RevisedProposal(), NoNames, AdLimitsEvaluation.Allow());

        detail.Versions.Select(v => v.Version).Should().Equal(2, 1);
        detail.Versions[1].DiffLines.Should().HaveCount(2);
        detail.Versions[1].Reasoning.Should().Be("first");
        detail.Approvals.Should().ContainSingle().Which.Version.Should().Be(1);
        detail.Executions.Should().BeEmpty();
        detail.CurrentEvaluation!.Decision.Should().Be(AdLimitsDecision.Allowed);
    }

    [Fact]
    public void ToDetail_WithoutEvaluation_LeavesItNull()
    {
        AdProposalDtoMapper.ToDetail(RevisedProposal(), NoNames, null).CurrentEvaluation.Should().BeNull();
    }

    [Fact]
    public void TargetIds_AreDistinctAcrossAllVersions()
    {
        AdProposalDtoMapper.TargetIds(RevisedProposal()).Should().BeEquivalentTo(new[] { "ad-1", "ad-2" });
    }

    [Fact]
    public void ToDetail_FlagsOnlyValidateOnlyFailuresAsValidateOnly()
    {
        var actions = new[] { ProposalTestHarness.PauseAction("ad-1"), ProposalTestHarness.PauseAction("ad-2") };
        var proposal = AdProposal.Create(new AdProposalDraft("GoogleAds", "acc-1", "PauseAd", "agent-1", AdActorKind.Agent,
            null, AdActionJson.Serialize(actions), actions.Length, "why"), Now, TimeSpan.FromHours(72));
        proposal.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: true);
        proposal.StartExecution(Now);
        proposal.RecordExecution(new AdProposalActionExecution
        {
            Version = 1, ActionIndex = 0, TargetExternalId = "ad-1", State = AdActionExecutionState.Failed,
            Error = $"{AdExecutionErrors.ValidateOnlyPrefix} Google accepted the request; nothing was changed.", ExecutedAt = Now,
        });
        proposal.RecordExecution(new AdProposalActionExecution
        {
            Version = 1, ActionIndex = 1, TargetExternalId = "ad-2", State = AdActionExecutionState.Failed, Error = "POLICY", ExecutedAt = Now,
        });

        var detail = AdProposalDtoMapper.ToDetail(proposal, NoNames, null);

        detail.Executions.Select(e => e.ValidateOnly).Should().Equal(true, false);
    }
}
