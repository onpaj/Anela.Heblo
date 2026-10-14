using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

public interface IAdLimitsContextBuilder
{
    /// <summary>Submit/approve perspective: other Approved and Executing proposals hold their pauses against the weekly cap.</summary>
    Task<AdLimitsRequest> BuildAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, ApprovalChannel channel, string? approverPrincipal, CancellationToken cancellationToken);

    /// <summary>
    /// Execution perspective (System channel, the executor principal): only other Executing proposals hold pauses;
    /// Approved ones queue behind this one and are re-checked when they run, so the earliest to run wins.
    /// </summary>
    Task<AdLimitsRequest> BuildForExecutionAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, CancellationToken cancellationToken);
}
