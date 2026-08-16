namespace Anela.Heblo.Domain.Features.MindMaps;

public class MindMap
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public MindMapStatus Status { get; set; } = MindMapStatus.Idle;
    public string CurrentJson { get; set; } = null!;
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<MindMapMeeting> Meetings { get; set; } = new();
    public List<MindMapVersion> Versions { get; set; } = new();
}
