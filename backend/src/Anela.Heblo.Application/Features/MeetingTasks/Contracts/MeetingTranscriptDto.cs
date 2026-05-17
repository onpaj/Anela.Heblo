namespace Anela.Heblo.Application.Features.MeetingTasks.Contracts;

public class MeetingTranscriptDto
{
    public Guid Id { get; set; }
    public string PlaudRecordingId { get; set; } = null!;
    public DateTime PlaudCreatedAt { get; set; }
    public string Subject { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string RawTranscript { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime ReceivedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUser { get; set; }
    public int TaskCount { get; set; }
    public int ApprovedTaskCount { get; set; }
    public int RejectedTaskCount { get; set; }
    public List<ProposedTaskDto> Tasks { get; set; } = new();
    public string AccessLevel { get; set; } = "Private";
    public List<MeetingAccessGrantDto> AccessGrants { get; set; } = new();
}
