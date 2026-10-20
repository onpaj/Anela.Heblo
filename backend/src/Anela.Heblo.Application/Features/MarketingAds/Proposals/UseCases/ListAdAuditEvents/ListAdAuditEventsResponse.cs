using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdAuditEvents;

public class ListAdAuditEventsResponse : BaseResponse
{
    public List<AdAuditEventDto> Items { get; set; } = new();
    public int TotalCount { get; set; }

    public ListAdAuditEventsResponse() { }
    public ListAdAuditEventsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
