using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapLockServiceTests
{
    private const string UserEmail = "ondra@anela.cz";
    private readonly MindMapLockService _service = new();

    private static MindMapDocument Current() => new()
    {
        RootNodeId = "root",
        Nodes = new List<MindMapNode>
        {
            new() { Id = "root", Title = "Projekt" },
            new()
            {
                Id = "a", ParentId = "root", Title = "Větev A",
                SourceMeetingIds = new List<Guid> { Guid.Parse("11111111-1111-1111-1111-111111111111") }
            },
            new() { Id = "b", ParentId = "root", Title = "Větev B", LockedBy = "jina@anela.cz" }
        }
    };

    [Fact]
    public void ApplyUserEdit_LocksNode_WhenTitleChanged()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.Single(n => n.Id == "a").Title = "Přejmenováno";

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        Assert.Equal(UserEmail, result.Nodes.Single(n => n.Id == "a").LockedBy);
    }

    [Fact]
    public void ApplyUserEdit_LocksNode_WhenNotesChanged()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.Single(n => n.Id == "a").Notes = "Nová poznámka";

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        Assert.Equal(UserEmail, result.Nodes.Single(n => n.Id == "a").LockedBy);
    }

    [Fact]
    public void ApplyUserEdit_LocksNode_WhenOwnerChanged()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.Single(n => n.Id == "a").Owner = "novy@anela.cz";

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        Assert.Equal(UserEmail, result.Nodes.Single(n => n.Id == "a").LockedBy);
    }

    [Fact]
    public void ApplyUserEdit_DoesNotLock_OnPositionStatusOrCollapseChange()
    {
        var submitted = MindMapJson.Clone(Current());
        var a = submitted.Nodes.Single(n => n.Id == "a");
        a.Position = new NodePosition { X = 5, Y = 5 };
        a.Collapsed = true;
        a.Status = MindMapNodeStatus.Done;

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        Assert.Null(result.Nodes.Single(n => n.Id == "a").LockedBy);
        Assert.Equal(MindMapNodeStatus.Done, result.Nodes.Single(n => n.Id == "a").Status);
        Assert.Equal(5, result.Nodes.Single(n => n.Id == "a").Position!.X);
    }

    [Fact]
    public void ApplyUserEdit_PreservesExistingLock_AndIgnoresClientLockTampering()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.Single(n => n.Id == "b").LockedBy = null;   // client tried to unlock
        submitted.Nodes.Single(n => n.Id == "a").LockedBy = "spoof@anela.cz"; // client tried to lock as someone else

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        Assert.Equal("jina@anela.cz", result.Nodes.Single(n => n.Id == "b").LockedBy);
        Assert.Null(result.Nodes.Single(n => n.Id == "a").LockedBy);
    }

    [Fact]
    public void ApplyUserEdit_AssignsServerIdAndLock_ToUserAddedNodes()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.Add(new MindMapNode { Id = "tmp-1", ParentId = "a", Title = "Nový uzel" });
        submitted.Nodes.Add(new MindMapNode { Id = "tmp-2", ParentId = "tmp-1", Title = "Vnořený" });

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        var added = result.Nodes.Single(n => n.Title == "Nový uzel");
        var nested = result.Nodes.Single(n => n.Title == "Vnořený");
        Assert.Equal(32, added.Id.Length);
        Assert.Equal(added.Id, nested.ParentId);
        Assert.Equal(UserEmail, added.LockedBy);
        Assert.Empty(added.SourceMeetingIds);
    }

    [Fact]
    public void ApplyUserEdit_PreservesSourceMeetingIdsFromCurrent()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.Single(n => n.Id == "a").SourceMeetingIds = new List<Guid>(); // client wiped provenance

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        Assert.Single(result.Nodes.Single(n => n.Id == "a").SourceMeetingIds);
    }

    [Fact]
    public void ApplyUserEdit_TombstonesDeletedNodes()
    {
        var submitted = MindMapJson.Clone(Current());
        submitted.Nodes.RemoveAll(n => n.Id == "a");

        var result = _service.ApplyUserEdit(Current(), submitted, UserEmail);

        var tombstone = result.SuppressedNodes.Single();
        Assert.Equal("Větev A", tombstone.Title);
        Assert.Equal(UserEmail, tombstone.DeletedBy);
    }

    [Fact]
    public void ApplyUserEdit_IgnoresClientSuppressedNodes_AndKeepsExistingOnes()
    {
        var current = Current();
        current.SuppressedNodes.Add(new SuppressedNode { Title = "Staré", DeletedBy = "jina@anela.cz" });
        var submitted = MindMapJson.Clone(current);
        submitted.SuppressedNodes = new List<SuppressedNode> { new() { Title = "Podvržené" } };

        var result = _service.ApplyUserEdit(current, submitted, UserEmail);

        Assert.Equal("Staré", result.SuppressedNodes.Single().Title);
    }

    [Fact]
    public void ApplyUserEdit_DeduplicatesTombstones_ByTrimmedCaseInsensitiveTitle()
    {
        var current = Current();
        current.Nodes.Single(n => n.Id == "a").Title = "Deleted Node";
        current.SuppressedNodes.Add(new SuppressedNode { Title = "  deleted node  ", DeletedBy = "jina@anela.cz" });
        var submitted = MindMapJson.Clone(current);
        submitted.Nodes.RemoveAll(n => n.Id == "a");

        var result = _service.ApplyUserEdit(current, submitted, UserEmail);

        Assert.Single(result.SuppressedNodes);
    }

    [Fact]
    public void ApplyUserEdit_CarriesOverSchemaVersionFromCurrent()
    {
        var current = Current();
        current.SchemaVersion = 2;
        var submitted = MindMapJson.Clone(current);
        submitted.SchemaVersion = 99;

        var result = _service.ApplyUserEdit(current, submitted, UserEmail);

        Assert.Equal(2, result.SchemaVersion);
    }

    [Fact]
    public void ApplyUserEdit_Throws_WhenUserEmailIsNull()
    {
        var current = Current();
        var submitted = MindMapJson.Clone(current);

        Assert.ThrowsAny<ArgumentException>(() => _service.ApplyUserEdit(current, submitted, null!));
    }

    [Fact]
    public void ApplyUserEdit_Throws_WhenUserEmailIsWhitespace()
    {
        var current = Current();
        var submitted = MindMapJson.Clone(current);

        Assert.ThrowsAny<ArgumentException>(() => _service.ApplyUserEdit(current, submitted, "   "));
    }

    [Fact]
    public void ApplyUserEdit_DoesNotMutateInputs()
    {
        var current = Current();
        var submitted = MindMapJson.Clone(current);
        submitted.Nodes.Single(n => n.Id == "a").Title = "Změna";

        var result = _service.ApplyUserEdit(current, submitted, UserEmail);

        Assert.Equal("Větev A", current.Nodes.Single(n => n.Id == "a").Title);
        Assert.Null(submitted.Nodes.Single(n => n.Id == "a").LockedBy);
        Assert.NotSame(
            current.Nodes.Single(n => n.Id == "a").SourceMeetingIds,
            result.Nodes.Single(n => n.Id == "a").SourceMeetingIds);
    }
}
