using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;

public class ApproveAdProposalRequest : IRequest<ApproveAdProposalResponse>
{
    /// <summary>Set from the route by the controller.</summary>
    public int Id { get; set; }
    /// <summary>Must equal the proposal's current version.</summary>
    public int Version { get; set; }
}
