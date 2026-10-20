using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;

public class ApproveAdProposalResponse : BaseResponse
{
    public int ProposalId { get; set; }
    public int Version { get; set; }
    public AdProposalStatus Status { get; set; }
    public bool AwaitingSecondApprover { get; set; }
    public string? ExecutionJobId { get; set; }

    public ApproveAdProposalResponse() { }

    public ApproveAdProposalResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
