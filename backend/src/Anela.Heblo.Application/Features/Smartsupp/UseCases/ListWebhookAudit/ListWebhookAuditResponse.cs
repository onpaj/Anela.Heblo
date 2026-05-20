using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ListWebhookAudit;

public class ListWebhookAuditResponse : BaseResponse
{
    public List<WebhookAuditSummaryDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Skip { get; set; }
    public int PageSize { get; set; }
}
