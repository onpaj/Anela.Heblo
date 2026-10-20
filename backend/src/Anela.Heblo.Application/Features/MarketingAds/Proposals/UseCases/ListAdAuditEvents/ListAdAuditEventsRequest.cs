using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdAuditEvents;

public class ListAdAuditEventsRequest : IRequest<ListAdAuditEventsResponse>
{
    public int? ProposalId { get; set; }
    public AdAuditEventType? EventType { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
