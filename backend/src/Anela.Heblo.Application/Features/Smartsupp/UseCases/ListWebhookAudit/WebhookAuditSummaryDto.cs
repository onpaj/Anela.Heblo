using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ListWebhookAudit;

public class WebhookAuditSummaryDto
{
    public Guid Id { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? EventName { get; set; }
    public string? AccountId { get; set; }
    public string? AppId { get; set; }
    public SmartsuppWebhookSignatureStatus SignatureStatus { get; set; }
    public SmartsuppWebhookProcessingStatus ProcessingStatus { get; set; }
    public int BodySizeBytes { get; set; }
    public int ProcessingDurationMs { get; set; }
    public int ReplayCount { get; set; }
    public DateTime? LastReplayedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
