using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RejectAdProposal;

public class RejectAdProposalRequest : IRequest<RejectAdProposalResponse>
{
    public const int MaxReasonLength = 2000;

    /// <summary>Set from the route by the controller.</summary>
    public int Id { get; set; }
    /// <summary>Must equal the proposal's current version.</summary>
    public int Version { get; set; }
    public string Reason { get; set; } = string.Empty;
}
