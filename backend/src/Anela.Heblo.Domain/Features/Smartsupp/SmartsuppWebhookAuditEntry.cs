namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppWebhookAuditEntry
{
    public Guid Id { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string EventName { get; set; } = null!;
    public string? AccountId { get; set; }
    public SmartsuppWebhookSignatureStatus SignatureStatus { get; set; }
    public string? SignatureHeader { get; set; }
    public SmartsuppWebhookProcessingStatus ProcessingStatus { get; set; }
    public string RawBody { get; set; } = null!;
    public int BodySizeBytes { get; set; }
    public string? HeadersJson { get; set; }
    public int? ProcessingDurationMs { get; set; }
    public string? ProcessingError { get; set; }
}
