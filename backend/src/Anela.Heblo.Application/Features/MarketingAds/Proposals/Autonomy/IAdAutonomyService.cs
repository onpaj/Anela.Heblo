using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;

public interface IAdAutonomyService
{
    /// <summary>Approves the (tracked, unsaved or loaded) proposal as the system principal when its autonomy mode allows it. Caller saves.</summary>
    Task<bool> TryAutoApproveAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, CancellationToken cancellationToken);
}
