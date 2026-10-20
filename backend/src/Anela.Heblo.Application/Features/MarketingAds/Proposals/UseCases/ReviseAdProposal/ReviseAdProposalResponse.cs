using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;

public class ReviseAdProposalResponse : AdProposalMutationResponse
{
    public ReviseAdProposalResponse() { }

    public ReviseAdProposalResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
