using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapGuardTests
{
    private readonly MindMapGuard _guard = new();
    private static readonly Guid MeetingId = Guid.NewGuid();

    private static MindMapDocument Previous() => new()
    {
        RootNodeId = "root",
        Nodes = new List<MindMapNode>
        {
            new() { Id = "root", Title = "Projekt" },
            new()
            {
                Id = "locked", ParentId = "root", Title = "Ruční název", Notes = "ruční poznámka",
                Owner = "Ondra", LockedBy = "ondra@anela.cz", Status = MindMapNodeStatus.Active,
                Position = new NodePosition { X = 100, Y = 50 }, Collapsed = true
            },
            new() { Id = "free", ParentId = "root", Title = "Volný uzel" }
        },
        SuppressedNodes = new List<SuppressedNode> { new() { Title = "Zrušený nápad", DeletedBy = "ondra@anela.cz" } }
    };

    /// <summary>LLM result echoing Previous() structure without UI metadata (as the LLM sees it).</summary>
    private static MindMapDocument LlmEcho() => new()
    {
        RootNodeId = "root",
        Nodes = new List<MindMapNode>
        {
            new() { Id = "root", Title = "Projekt" },
            new() { Id = "locked", ParentId = "root", Title = "Ruční název", Notes = "ruční poznámka", Owner = "Ondra" },
            new() { Id = "free", ParentId = "root", Title = "Volný uzel" }
        }
    };

    [Fact]
    public void ApplyLlmUpdate_RestoresLockedNodeContent_WhenLlmRewroteIt()
    {
        var llm = LlmEcho();
        var lockedInLlm = llm.Nodes.Single(n => n.Id == "locked");
        lockedInLlm.Title = "Přepsáno LLM";
        lockedInLlm.Notes = "jiná poznámka";
        lockedInLlm.Owner = "Nikdo";

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        var locked = result.Nodes.Single(n => n.Id == "locked");
        Assert.Equal("Ruční název", locked.Title);
        Assert.Equal("ruční poznámka", locked.Notes);
        Assert.Equal("Ondra", locked.Owner);
    }

    [Fact]
    public void ApplyLlmUpdate_AllowsStatusChangeOnLockedNode()
    {
        var llm = LlmEcho();
        llm.Nodes.Single(n => n.Id == "locked").Status = MindMapNodeStatus.Done;

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        Assert.Equal(MindMapNodeStatus.Done, result.Nodes.Single(n => n.Id == "locked").Status);
    }

    [Fact]
    public void ApplyLlmUpdate_ReinsertsDeletedLockedNode()
    {
        var llm = LlmEcho();
        llm.Nodes.RemoveAll(n => n.Id == "locked");

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        var locked = result.Nodes.Single(n => n.Id == "locked");
        Assert.Equal("root", locked.ParentId);
        Assert.Equal("Ruční název", locked.Title);
        Assert.Equal("ondra@anela.cz", locked.LockedBy);
    }

    [Fact]
    public void ApplyLlmUpdate_ReinsertsLockedNodeUnderNearestSurvivingAncestor()
    {
        var previous = Previous();
        // deep chain: root -> free -> mid -> deepLocked
        previous.Nodes.Add(new MindMapNode { Id = "mid", ParentId = "free", Title = "Mezi" });
        previous.Nodes.Add(new MindMapNode
        {
            Id = "deepLocked",
            ParentId = "mid",
            Title = "Hluboký",
            LockedBy = "ondra@anela.cz"
        });
        var llm = LlmEcho(); // LLM dropped both "mid" and "deepLocked"; "free" survives

        var result = _guard.ApplyLlmUpdate(previous, llm, MeetingId);

        Assert.Equal("free", result.Nodes.Single(n => n.Id == "deepLocked").ParentId);
    }

    [Fact]
    public void ApplyLlmUpdate_RemovesRecreatedSuppressedNode_AndReparentsItsChildren()
    {
        var llm = LlmEcho();
        llm.Nodes.Add(new MindMapNode { Id = "new-1", ParentId = "root", Title = "Zrušený nápad" });
        llm.Nodes.Add(new MindMapNode { Id = "new-2", ParentId = "new-1", Title = "Dítě zombie uzlu" });

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        Assert.DoesNotContain(result.Nodes, n => n.Title == "Zrušený nápad");
        Assert.Equal("root", result.Nodes.Single(n => n.Title == "Dítě zombie uzlu").ParentId);
    }

    [Fact]
    public void ApplyLlmUpdate_AssignsServerIdsAndMeetingSource_ToNewNodes()
    {
        var llm = LlmEcho();
        llm.Nodes.Add(new MindMapNode { Id = "new-1", ParentId = "free", Title = "Nová větev" });
        llm.Nodes.Add(new MindMapNode { Id = "new-2", ParentId = "new-1", Title = "Nový list" });

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        var branch = result.Nodes.Single(n => n.Title == "Nová větev");
        var leaf = result.Nodes.Single(n => n.Title == "Nový list");
        Assert.NotEqual("new-1", branch.Id);
        Assert.Equal(32, branch.Id.Length); // Guid "N" format
        Assert.Equal(branch.Id, leaf.ParentId);
        Assert.Contains(MeetingId, branch.SourceMeetingIds);
        Assert.Null(branch.Position);
        Assert.Null(branch.LockedBy);
    }

    [Fact]
    public void ApplyLlmUpdate_CarriesOverUiMetadataByNodeId()
    {
        var result = _guard.ApplyLlmUpdate(Previous(), LlmEcho(), MeetingId);

        var locked = result.Nodes.Single(n => n.Id == "locked");
        Assert.Equal(100, locked.Position!.X);
        Assert.True(locked.Collapsed);
        Assert.Equal("ondra@anela.cz", locked.LockedBy);
    }

    [Fact]
    public void ApplyLlmUpdate_CarriesOverSuppressedNodesVerbatim()
    {
        var llm = LlmEcho();
        llm.SuppressedNodes = new List<SuppressedNode>(); // LLM tried to clear tombstones

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        Assert.Equal("Zrušený nápad", result.SuppressedNodes.Single().Title);
    }

    [Fact]
    public void ApplyLlmUpdate_CarriesOverSchemaVersion_IgnoringLlmTampering()
    {
        var llm = LlmEcho();
        llm.SchemaVersion = 99;

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        Assert.Equal(1, result.SchemaVersion);
    }

    [Fact]
    public void ApplyLlmUpdate_PreservesExistingProvenance_WhenLlmOmitsSourceMeetingIds()
    {
        var previous = Previous();
        var meeting1 = Guid.NewGuid();
        previous.Nodes.Single(n => n.Id == "free").SourceMeetingIds = new List<Guid> { meeting1 };
        var llm = LlmEcho(); // "free" node comes back from the LLM with no SourceMeetingIds at all

        var result = _guard.ApplyLlmUpdate(previous, llm, MeetingId);

        Assert.Contains(meeting1, result.Nodes.Single(n => n.Id == "free").SourceMeetingIds);
    }

    [Fact]
    public void ApplyLlmUpdate_UnionsProvenance_WhenLlmAttributesExistingNodeToNewMeeting()
    {
        var previous = Previous();
        var meeting1 = Guid.NewGuid();
        var meeting2 = Guid.NewGuid();
        previous.Nodes.Single(n => n.Id == "free").SourceMeetingIds = new List<Guid> { meeting1 };
        var llm = LlmEcho();
        llm.Nodes.Single(n => n.Id == "free").SourceMeetingIds = new List<Guid> { meeting2 };

        var result = _guard.ApplyLlmUpdate(previous, llm, MeetingId);

        var freeSourceMeetingIds = result.Nodes.Single(n => n.Id == "free").SourceMeetingIds;
        Assert.Contains(meeting1, freeSourceMeetingIds);
        Assert.Contains(meeting2, freeSourceMeetingIds);
    }

    [Fact]
    public void ApplyLlmUpdate_Throws_WhenLlmReturnsDuplicateNodeIds()
    {
        var llm = LlmEcho();
        llm.Nodes.Add(new MindMapNode { Id = "free", ParentId = "root", Title = "Duplicitní" });

        Assert.Throws<MindMapGuardException>(() => _guard.ApplyLlmUpdate(Previous(), llm, MeetingId));
    }

    [Fact]
    public void ApplyLlmUpdate_Throws_WhenLlmReturnsNodeWithEmptyId()
    {
        var llm = LlmEcho();
        llm.Nodes.Add(new MindMapNode { Id = "   ", ParentId = "root", Title = "Bez id" });

        Assert.Throws<MindMapGuardException>(() => _guard.ApplyLlmUpdate(Previous(), llm, MeetingId));
    }

    [Fact]
    public void ApplyLlmUpdate_Throws_WhenLlmReturnsNewNodeWithNullTitle()
    {
        var llm = LlmEcho();
        llm.Nodes.Add(new MindMapNode { Id = "new-1", ParentId = "root", Title = null! });

        Assert.Throws<MindMapGuardException>(() => _guard.ApplyLlmUpdate(Previous(), llm, MeetingId));
    }

    [Fact]
    public void ApplyLlmUpdate_Throws_WhenLlmReturnsExistingNonLockedNodeWithNullTitle()
    {
        var llm = LlmEcho();
        llm.Nodes.Single(n => n.Id == "free").Title = null!;

        Assert.Throws<MindMapGuardException>(() => _guard.ApplyLlmUpdate(Previous(), llm, MeetingId));
    }

    [Fact]
    public void ApplyLlmUpdate_RestoresLockedNodeTitle_WhenLlmEchoesNullTitle()
    {
        var llm = LlmEcho();
        llm.Nodes.Single(n => n.Id == "locked").Title = null!;

        var result = _guard.ApplyLlmUpdate(Previous(), llm, MeetingId);

        Assert.Equal("Ruční název", result.Nodes.Single(n => n.Id == "locked").Title);
    }

    [Fact]
    public void ApplyLlmUpdate_Throws_WhenPreviousHasSuppressedNodeWithNullTitle()
    {
        var previous = Previous();
        previous.SuppressedNodes.Add(new SuppressedNode { Title = null!, DeletedBy = "ondra@anela.cz" });

        Assert.Throws<MindMapGuardException>(() => _guard.ApplyLlmUpdate(previous, LlmEcho(), MeetingId));
    }

    [Fact]
    public void ApplyLlmUpdate_Throws_WhenRootIdChanged()
    {
        var llm = LlmEcho();
        llm.RootNodeId = "free";
        llm.Nodes.Single(n => n.Id == "root").ParentId = "free";
        llm.Nodes.Single(n => n.Id == "free").ParentId = null;

        Assert.Throws<MindMapGuardException>(() => _guard.ApplyLlmUpdate(Previous(), llm, MeetingId));
    }

    [Fact]
    public void ApplyLlmUpdate_Throws_WhenMergedResultIsInvalid()
    {
        var llm = LlmEcho();
        llm.Nodes.Single(n => n.Id == "free").ParentId = "ghost";

        Assert.Throws<MindMapGuardException>(() => _guard.ApplyLlmUpdate(Previous(), llm, MeetingId));
    }

    [Fact]
    public void ApplyLlmUpdate_DoesNotMutateInputs()
    {
        var previous = Previous();
        var llm = LlmEcho();
        llm.Nodes.Add(new MindMapNode { Id = "new-1", ParentId = "root", Title = "Nový" });

        _guard.ApplyLlmUpdate(previous, llm, MeetingId);

        Assert.Equal(3, previous.Nodes.Count);
        Assert.Equal("new-1", llm.Nodes.Single(n => n.Title == "Nový").Id);
        // Pins by-value copying, not just by-reference passthrough (e.g. MergeUiMetadata
        // assigning prev.Position directly would let a later write to the result mutate this).
        Assert.Equal(100, previous.Nodes.Single(n => n.Id == "locked").Position!.X);
    }
}
