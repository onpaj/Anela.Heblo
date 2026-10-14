using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RejectAdProposal;

public class RejectAdProposalResponse : BaseResponse
{
    public int ProposalId { get; set; }
    public int Version { get; set; }
    public AdProposalStatus Status { get; set; }

    public RejectAdProposalResponse() { }

    public RejectAdProposalResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
