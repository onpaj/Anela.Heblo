using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAdAuditEvents;

public class ListAdAuditEventsHandler : IRequestHandler<ListAdAuditEventsRequest, ListAdAuditEventsResponse>
{
    private readonly IAdAuditLog _audit;

    public ListAdAuditEventsHandler(IAdAuditLog audit) => _audit = audit;

    public async Task<ListAdAuditEventsResponse> Handle(ListAdAuditEventsRequest request, CancellationToken cancellationToken)
    {
        var (items, total) = await _audit.ListAsync(new AdAuditQuery
        {
            ProposalId = request.ProposalId,
            EventType = request.EventType,
            Page = request.Page,
            PageSize = request.PageSize,
        }, cancellationToken);
        return new ListAdAuditEventsResponse { Items = items.Select(AdProposalDtoMapper.ToDto).ToList(), TotalCount = total };
    }
}
