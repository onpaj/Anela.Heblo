using Anela.Heblo.Application.Features.MindMaps.Model;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

public class MindMapDocumentValidatorTests
{
    private static MindMapDocument ValidDoc() => new()
    {
        RootNodeId = "root",
        Nodes = new List<MindMapNode>
        {
            new() { Id = "root", Title = "Projekt" },
            new() { Id = "a", ParentId = "root", Title = "Větev A" },
            new() { Id = "b", ParentId = "a", Title = "List B" }
        }
    };

    [Fact]
    public void Validate_ReturnsNoErrors_ForValidTree()
    {
        Assert.Empty(MindMapDocumentValidator.Validate(ValidDoc()));
    }

    [Fact]
    public void Validate_Fails_WhenDocumentHasNoNodes()
    {
        var errors = MindMapDocumentValidator.Validate(new MindMapDocument { RootNodeId = "x" });
        Assert.Contains(errors, e => e.Contains("no nodes"));
    }

    [Fact]
    public void Validate_Fails_OnDuplicateNodeIds()
    {
        var doc = ValidDoc();
        doc.Nodes.Add(new MindMapNode { Id = "a", ParentId = "root", Title = "Dup" });
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("Duplicate node id 'a'"));
    }

    [Fact]
    public void Validate_Fails_WhenMoreThanOneRoot()
    {
        var doc = ValidDoc();
        doc.Nodes.Add(new MindMapNode { Id = "r2", ParentId = null, Title = "Second root" });
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("exactly one root"));
    }

    [Fact]
    public void Validate_Fails_WhenRootNodeIdDoesNotMatchParentlessNode()
    {
        var doc = ValidDoc();
        doc.RootNodeId = "a";
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("does not match"));
    }

    [Fact]
    public void Validate_Fails_OnMissingParentReference()
    {
        var doc = ValidDoc();
        doc.Nodes.Add(new MindMapNode { Id = "orphan", ParentId = "ghost", Title = "Orphan" });
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("missing parent 'ghost'"));
    }

    [Fact]
    public void Validate_Fails_OnParentCycle()
    {
        var doc = ValidDoc();
        doc.Nodes.Single(n => n.Id == "a").ParentId = "b"; // a -> b -> a
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("Cycle"));
    }

    [Fact]
    public void Validate_Fails_OnUnknownStatus()
    {
        var doc = ValidDoc();
        doc.Nodes[1].Status = "wip";
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("unknown status 'wip'"));
    }

    [Fact]
    public void Validate_Fails_OnEmptyTitle()
    {
        var doc = ValidDoc();
        doc.Nodes[1].Title = "  ";
        Assert.Contains(MindMapDocumentValidator.Validate(doc), e => e.Contains("empty title"));
    }
}
