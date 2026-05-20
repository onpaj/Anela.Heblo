namespace SmartsuppWebhookReplay.Models;

public sealed class AuditDetail
{
    public Guid Id { get; init; }
    public DateTime ReceivedAt { get; init; }
    public string? EventName { get; init; }
    public string? AccountId { get; init; }
    public string SignatureStatus { get; init; } = null!;
    public string? SignatureHeader { get; init; }
    public string ProcessingStatus { get; init; } = null!;
    public string RawBody { get; init; } = null!;
    public int BodySizeBytes { get; init; }
    public string? HeadersJson { get; init; }
    public int ProcessingDurationMs { get; init; }
    public string? ProcessingError { get; init; }
}
