namespace Anela.Heblo.Domain.Features.MeetingTasks;

public class MeetingTranscript
{
    public Guid Id { get; set; }
    public string PlaudRecordingId { get; set; } = null!;
    public DateTime PlaudCreatedAt { get; set; }
    public string Subject { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string RawTranscript { get; set; } = null!;
    public MeetingTranscriptStatus Status { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUser { get; set; }
    public List<ProposedTask> Tasks { get; set; } = new();
    public MeetingAccessLevel AccessLevel { get; set; } = MeetingAccessLevel.Private;
    public List<MeetingAccessGrant> AccessGrants { get; set; } = new();
}
