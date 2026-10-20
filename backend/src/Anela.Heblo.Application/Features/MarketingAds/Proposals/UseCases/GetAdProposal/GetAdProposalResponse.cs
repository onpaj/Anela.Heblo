using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdProposal;

public class GetAdProposalResponse : BaseResponse
{
    public AdProposalDetailDto Proposal { get; set; } = new();

    public GetAdProposalResponse() { }
    public GetAdProposalResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
