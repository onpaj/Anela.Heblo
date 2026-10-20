using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdProposals;

public class ListAdProposalsRequest : IRequest<ListAdProposalsResponse>
{
    public AdProposalStatus? Status { get; set; }
    public AdPlatform? Platform { get; set; }
    /// <summary>Only proposals created by the caller.</summary>
    public bool Mine { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    /// <summary>Oldest first (ascending creation time) instead of the default newest first. Last on
    /// purpose: the generated client takes query parameters positionally.</summary>
    public bool OldestFirst { get; set; }
}
