using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases;

/// <summary>Shared body of the Submit and Revise responses: the proposal's state right after the change.</summary>
public abstract class AdProposalMutationResponse : BaseResponse
{
    public int ProposalId { get; set; }
    public int Version { get; set; }
    public AdProposalStatus Status { get; set; }
    public bool AutoApproved { get; set; }
    public AdLimitsEvaluationDto Evaluation { get; set; } = new();
    /// <summary>Rendered by Heblo from the stored actions, never from the reasoning.</summary>
    public List<string> DiffLines { get; set; } = new();

    protected AdProposalMutationResponse() { }

    protected AdProposalMutationResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
