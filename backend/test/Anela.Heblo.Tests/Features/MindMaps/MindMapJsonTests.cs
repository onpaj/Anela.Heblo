using Anela.Heblo.Application.Features.MindMaps.Model;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapJsonTests
{
    [Fact]
    public void Serialize_UsesCamelCasePropertyNames()
    {
        var doc = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode> { new() { Id = "root", Title = "Test" } }
        };

        var json = MindMapJson.Serialize(doc);

        Assert.Contains("\"rootNodeId\":\"root\"", json);
        Assert.Contains("\"lockedBy\":null", json);
        Assert.DoesNotContain("RootNodeId", json);
    }

    [Fact]
    public void Deserialize_RoundTripsAllNodeFields()
    {
        var meetingId = Guid.NewGuid();
        var doc = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode>
            {
                new() { Id = "root", Title = "Projekt" },
                new()
                {
                    Id = "n1", ParentId = "root", Title = "Web", Notes = "poznámka",
                    Status = MindMapNodeStatus.Blocked, Owner = "Ondra", LockedBy = "ondra@anela.cz",
                    SourceMeetingIds = new List<Guid> { meetingId },
                    Position = new NodePosition { X = 12.5, Y = -3 }, Collapsed = true
                }
            },
            SuppressedNodes = new List<SuppressedNode> { new() { Title = "Smazané", DeletedBy = "ondra@anela.cz" } }
        };

        var restored = MindMapJson.Deserialize(MindMapJson.Serialize(doc));

        var n1 = restored.Nodes.Single(n => n.Id == "n1");
        Assert.Equal("Web", n1.Title);
        Assert.Equal("poznámka", n1.Notes);
        Assert.Equal(MindMapNodeStatus.Blocked, n1.Status);
        Assert.Equal("ondra@anela.cz", n1.LockedBy);
        Assert.Equal(meetingId, n1.SourceMeetingIds.Single());
        Assert.Equal(12.5, n1.Position!.X);
        Assert.True(n1.Collapsed);
        Assert.Equal("Smazané", restored.SuppressedNodes.Single().Title);
    }

    [Fact]
    public void Clone_ReturnsIndependentCopy()
    {
        var doc = new MindMapDocument
        {
            RootNodeId = "root",
            Nodes = new List<MindMapNode> { new() { Id = "root", Title = "Original" } }
        };

        var clone = MindMapJson.Clone(doc);
        clone.Nodes[0].Title = "Changed";

        Assert.Equal("Original", doc.Nodes[0].Title);
    }
}
