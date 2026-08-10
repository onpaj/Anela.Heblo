namespace Anela.Heblo.Domain.Features.MindMaps;

public class MindMapVersion
{
    public Guid Id { get; set; }
    public Guid MindMapId { get; set; }
    public int VersionNumber { get; set; }
    public string Json { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public Guid? TriggerMeetingId { get; set; }
    public MindMap MindMap { get; set; } = null!;
}
