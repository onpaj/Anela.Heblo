using Anela.Heblo.Application.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GroupCycleCheckTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    [Fact]
    public void WouldCreateCycle_DirectSelfParent_True()
    {
        GroupCycleCheck.WouldCreateCycle(A, new[] { A }, new Dictionary<Guid, List<Guid>>())
            .Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_BackEdge_True()
    {
        // Existing: B -> A (B has parent A). Adding A -> B closes a cycle.
        var existing = new Dictionary<Guid, List<Guid>> { [B] = new() { A } };
        GroupCycleCheck.WouldCreateCycle(A, new[] { B }, existing).Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_TransitiveBackEdge_True()
    {
        // Existing: B -> C, C -> A. Adding A -> B closes A->B->C->A.
        var existing = new Dictionary<Guid, List<Guid>> { [B] = new() { C }, [C] = new() { A } };
        GroupCycleCheck.WouldCreateCycle(A, new[] { B }, existing).Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_AcyclicParent_False()
    {
        var existing = new Dictionary<Guid, List<Guid>> { [B] = new() { C } };
        GroupCycleCheck.WouldCreateCycle(A, new[] { B }, existing).Should().BeFalse();
    }
}
