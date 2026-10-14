using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;

/// <summary>
/// Also carried by the MarketingAdsRevertFailed (502) response, so the UI can tell what is left to revert.
/// </summary>
public class RevertAdProposalResponse : BaseResponse
{
    public int ProposalId { get; set; }
    public AdProposalStatus Status { get; set; }
    /// <summary>Actions reverted by this request.</summary>
    public int RevertedActions { get; set; }
    /// <summary>Actions whose revert failed in this request; a retry reverts only these.</summary>
    public int FailedActions { get; set; }
    /// <summary>
    /// Of <see cref="FailedActions"/>, those the platform only validated (non-production): nothing changed in the
    /// account, so the UI says "validate-only" rather than reporting a platform failure.
    /// </summary>
    public int ValidateOnlyActions { get; set; }

    public RevertAdProposalResponse() { }

    public RevertAdProposalResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
