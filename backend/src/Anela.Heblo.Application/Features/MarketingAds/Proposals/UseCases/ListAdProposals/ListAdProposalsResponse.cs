using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdProposals;

public class ListAdProposalsResponse : BaseResponse
{
    public List<AdProposalSummaryDto> Items { get; set; } = new();
    public int TotalCount { get; set; }

    public ListAdProposalsResponse() { }
    public ListAdProposalsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
