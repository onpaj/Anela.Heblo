namespace Anela.Heblo.Application.Features.MindMaps.Contracts;

public class MindMapListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Status { get; set; } = null!;
    public int MeetingCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AttachedMeetingDto
{
    public Guid MeetingTranscriptId { get; set; }
    public string Subject { get; set; } = null!;
    public DateTime PlaudCreatedAt { get; set; }
    public DateTime AttachedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public class MindMapVersionDto
{
    public int VersionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? TriggerMeetingId { get; set; }
    public string? TriggerMeetingSubject { get; set; }
}
