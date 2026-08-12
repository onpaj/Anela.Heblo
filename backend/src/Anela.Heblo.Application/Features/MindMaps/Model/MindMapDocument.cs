namespace Anela.Heblo.Application.Features.MindMaps.Model;

/// <summary>
/// The whole mind map document persisted as one jsonb value (camelCase on the wire).
/// Position/Collapsed/LockedBy are UI/system metadata the LLM never writes —
/// MindMapGuard restores them from the previous version after every LLM update.
/// </summary>
public class MindMapDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string RootNodeId { get; set; } = null!;
    public List<MindMapNode> Nodes { get; set; } = new();
    public List<SuppressedNode> SuppressedNodes { get; set; } = new();
}

public class MindMapNode
{
    public string Id { get; set; } = null!;
    public string? ParentId { get; set; }
    public string Title { get; set; } = null!;
    public string? Notes { get; set; }
    public string Status { get; set; } = MindMapNodeStatus.Active;
    public string? Owner { get; set; }
    public string? LockedBy { get; set; }
    public List<Guid> SourceMeetingIds { get; set; } = new();
    public NodePosition? Position { get; set; }
    public bool Collapsed { get; set; }
}

public class NodePosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

public class SuppressedNode
{
    public string Title { get; set; } = null!;
    public string? DeletedBy { get; set; }
}

public static class MindMapNodeStatus
{
    public const string Active = "active";
    public const string Done = "done";
    public const string Blocked = "blocked";
    public const string Idea = "idea";

    public static readonly IReadOnlyList<string> All = new[] { Active, Done, Blocked, Idea };
}
