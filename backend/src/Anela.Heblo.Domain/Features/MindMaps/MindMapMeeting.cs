using Anela.Heblo.Domain.Features.MeetingTasks;

namespace Anela.Heblo.Domain.Features.MindMaps;

public class MindMapMeeting
{
    public Guid Id { get; set; }
    public Guid MindMapId { get; set; }
    public Guid MeetingTranscriptId { get; set; }
    public DateTime AttachedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public MindMap MindMap { get; set; } = null!;
    public MeetingTranscript MeetingTranscript { get; set; } = null!;
}
