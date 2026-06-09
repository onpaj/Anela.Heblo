using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GroupClosureTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static GroupPermission Perm(Guid g, string v) => new() { GroupId = g, PermissionValue = v };
    private static GroupParent Parent(Guid child, Guid parent) => new() { GroupId = child, ParentGroupId = parent };

    [Fact]
    public void Resolve_UnionsDirectGroupPermissions()
    {
        var perms = new[] { Perm(A, "products.catalog.read"), Perm(B, "products.journal.read") };
        var result = GroupClosure.Resolve(new[] { A }, perms, Array.Empty<GroupParent>());
        result.Should().BeEquivalentTo(new[] { "products.catalog.read" });
    }

    [Fact]
    public void Resolve_IncludesParentPermissions()
    {
        var perms = new[] { Perm(A, "products.catalog.read"), Perm(B, "products.journal.read") };
        var parents = new[] { Parent(A, B) }; // A inherits B
        var result = GroupClosure.Resolve(new[] { A }, perms, parents);
        result.Should().BeEquivalentTo(new[] { "products.catalog.read", "products.journal.read" });
    }

    [Fact]
    public void Resolve_DeepChain_AccumulatesAllAncestors()
    {
        var perms = new[] { Perm(A, "a.read"), Perm(B, "b.read"), Perm(C, "c.read") };
        var parents = new[] { Parent(A, B), Parent(B, C) }; // A -> B -> C
        var result = GroupClosure.Resolve(new[] { A }, perms, parents);
        result.Should().BeEquivalentTo(new[] { "a.read", "b.read", "c.read" });
    }

    [Fact]
    public void Resolve_DiamondParent_CountsOnce_NoError()
    {
        var perms = new[] { Perm(A, "a.read"), Perm(B, "b.read"), Perm(C, "c.read") };
        // Diamond: A -> B, A -> C, B -> C
        var parents = new[] { Parent(A, B), Parent(A, C), Parent(B, C) };
        var result = GroupClosure.Resolve(new[] { A }, perms, parents);
        result.Should().BeEquivalentTo(new[] { "a.read", "b.read", "c.read" });
    }

    [Fact]
    public void Resolve_Cycle_Terminates_WithBoundedSet()
    {
        var perms = new[] { Perm(A, "a.read"), Perm(B, "b.read") };
        var parents = new[] { Parent(A, B), Parent(B, A) }; // cycle
        var result = GroupClosure.Resolve(new[] { A }, perms, parents);
        result.Should().BeEquivalentTo(new[] { "a.read", "b.read" });
    }

    [Fact]
    public void Resolve_NoGroups_ReturnsEmpty()
    {
        var result = GroupClosure.Resolve(Array.Empty<Guid>(), Array.Empty<GroupPermission>(), Array.Empty<GroupParent>());
        result.Should().BeEmpty();
    }
}
