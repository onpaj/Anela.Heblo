using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetWebhookAuditEntry;

public class GetWebhookAuditEntryRequest : IRequest<GetWebhookAuditEntryResponse>
{
    public Guid Id { get; set; }
}
