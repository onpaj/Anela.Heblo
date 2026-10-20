namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Lifecycle of an ad proposal (spec 6.2). Transitions are guarded by <see cref="AdProposalStateMachine"/>.</summary>
public enum AdProposalStatus
{
    Pending = 1,
    Approved = 2,
    Executing = 3,
    Executed = 4,
    PartiallyExecuted = 5,
    Failed = 6,
    Rejected = 7,
    Expired = 8,
    Reverted = 9,
}
