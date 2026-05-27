namespace SmartsuppWebhookReplay.Models;

public sealed class AuditSummary
{
    public Guid Id { get; init; }
    public DateTime ReceivedAt { get; init; }
    public string? EventName { get; init; }
    public string? AccountId { get; init; }
    public string SignatureStatus { get; init; } = null!;
    public string ProcessingStatus { get; init; } = null!;
    public int BodySizeBytes { get; init; }
    public int ProcessingDurationMs { get; init; }
}
