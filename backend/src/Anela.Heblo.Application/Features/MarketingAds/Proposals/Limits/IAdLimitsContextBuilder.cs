using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

public interface IAdLimitsContextBuilder
{
    Task<AdLimitsRequest> BuildAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, ApprovalChannel channel, string? approverPrincipal, CancellationToken cancellationToken);
}
