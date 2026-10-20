using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SubmitAdProposal;

public class SubmitAdProposalResponse : AdProposalMutationResponse
{
    public SubmitAdProposalResponse() { }

    public SubmitAdProposalResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
