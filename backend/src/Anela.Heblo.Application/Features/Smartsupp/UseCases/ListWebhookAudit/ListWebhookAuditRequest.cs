using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ListWebhookAudit;

public class ListWebhookAuditRequest : IRequest<ListWebhookAuditResponse>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? EventName { get; set; }
    public SmartsuppWebhookSignatureStatus? SignatureStatus { get; set; }
    public SmartsuppWebhookProcessingStatus? ProcessingStatus { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}
