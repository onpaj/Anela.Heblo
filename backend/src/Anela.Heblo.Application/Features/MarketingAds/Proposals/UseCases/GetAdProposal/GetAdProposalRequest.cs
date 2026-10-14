using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdProposal;

public class GetAdProposalRequest : IRequest<GetAdProposalResponse>
{
    public int Id { get; set; }
}
