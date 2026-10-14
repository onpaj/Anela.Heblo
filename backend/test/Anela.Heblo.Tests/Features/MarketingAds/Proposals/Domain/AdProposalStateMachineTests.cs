using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Domain;

public class AdProposalStateMachineTests
{
    private static readonly HashSet<(AdProposalStatus, AdProposalStatus)> Allowed = new()
    {
        (AdProposalStatus.Pending, AdProposalStatus.Approved),
        (AdProposalStatus.Pending, AdProposalStatus.Rejected),
        (AdProposalStatus.Pending, AdProposalStatus.Expired),
        (AdProposalStatus.Approved, AdProposalStatus.Executing),
        (AdProposalStatus.Executing, AdProposalStatus.Executed),
        (AdProposalStatus.Executing, AdProposalStatus.PartiallyExecuted),
        (AdProposalStatus.Executing, AdProposalStatus.Failed),
        (AdProposalStatus.Executed, AdProposalStatus.Reverted),
        (AdProposalStatus.PartiallyExecuted, AdProposalStatus.Reverted),
    };

    public static TheoryData<AdProposalStatus, AdProposalStatus> AllPairs()
    {
        var data = new TheoryData<AdProposalStatus, AdProposalStatus>();
        foreach (var from in Enum.GetValues<AdProposalStatus>())
            foreach (var to in Enum.GetValues<AdProposalStatus>())
                data.Add(from, to);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void CanTransition_MatchesTheSpecLifecycle(AdProposalStatus from, AdProposalStatus to)
    {
        AdProposalStateMachine.CanTransition(from, to).Should().Be(Allowed.Contains((from, to)));
    }
}
