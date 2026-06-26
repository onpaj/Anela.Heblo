using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetWebhookAuditEntry;

public class GetWebhookAuditEntryResponse : BaseResponse
{
    public WebhookAuditEntryDto? Entry { get; set; }

    public GetWebhookAuditEntryResponse() { }

    public GetWebhookAuditEntryResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class WebhookAuditEntryDto
{
    public Guid Id { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string RemoteIp { get; set; } = "";
    public string? SignatureHeader { get; set; }
    public SmartsuppWebhookSignatureStatus SignatureStatus { get; set; }
    public string HeadersJson { get; set; } = "";
    public string RawBody { get; set; } = "";
    public int BodySizeBytes { get; set; }
    public string? EventName { get; set; }
    public string? AccountId { get; set; }
    public string? AppId { get; set; }
    public DateTime? EventTimestamp { get; set; }
    public SmartsuppWebhookProcessingStatus ProcessingStatus { get; set; }
    public string? ProcessingError { get; set; }
    public int ProcessingDurationMs { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int ReplayCount { get; set; }
    public DateTime? LastReplayedAt { get; set; }
    public string? LastReplayedBy { get; set; }
}
