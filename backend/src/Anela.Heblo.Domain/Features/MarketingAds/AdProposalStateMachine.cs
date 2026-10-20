namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Allowed status transitions (spec 6.2). A revision keeps the proposal Pending and is not a transition.</summary>
public static class AdProposalStateMachine
{
    private static readonly IReadOnlyDictionary<AdProposalStatus, AdProposalStatus[]> Allowed =
        new Dictionary<AdProposalStatus, AdProposalStatus[]>
        {
            [AdProposalStatus.Pending] = new[] { AdProposalStatus.Approved, AdProposalStatus.Rejected, AdProposalStatus.Expired },
            [AdProposalStatus.Approved] = new[] { AdProposalStatus.Executing },
            [AdProposalStatus.Executing] = new[] { AdProposalStatus.Executed, AdProposalStatus.PartiallyExecuted, AdProposalStatus.Failed },
            [AdProposalStatus.Executed] = new[] { AdProposalStatus.Reverted },
            [AdProposalStatus.PartiallyExecuted] = new[] { AdProposalStatus.Reverted },
        };

    public static bool CanTransition(AdProposalStatus from, AdProposalStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);
}
