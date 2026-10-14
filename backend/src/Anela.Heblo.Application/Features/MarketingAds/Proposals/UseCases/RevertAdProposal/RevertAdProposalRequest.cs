using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;

public class RevertAdProposalRequest : IRequest<RevertAdProposalResponse>
{
    /// <summary>Set from the route by the controller.</summary>
    public int Id { get; set; }
}
