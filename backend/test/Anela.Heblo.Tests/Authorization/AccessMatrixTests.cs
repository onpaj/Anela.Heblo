using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class DeterministicGuidTests
{
    [Fact]
    public void ForRole_IsStable_ForSameValue()
    {
        var a = DeterministicGuid.ForRole("purchase_orders.read");
        var b = DeterministicGuid.ForRole("purchase_orders.read");
        a.Should().Be(b);
        a.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ForRole_Differs_ForDifferentValues()
    {
        DeterministicGuid.ForRole("purchase_orders.read")
            .Should().NotBe(DeterministicGuid.ForRole("purchase_orders.write"));
    }

    [Fact]
    public void ForRole_ThrowsOnNull()
    {
        var act = () => DeterministicGuid.ForRole(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

public class AccessMatrixConsistencyTests
{
    [Fact]
    public void RoleValues_AreUnique()
    {
        var values = AccessMatrix.AllRoleValues().ToList();
        values.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void RoleGuids_AreUnique()
    {
        var guids = AccessMatrix.AllRoleValues().Select(DeterministicGuid.ForRole).ToList();
        guids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryGroupRole_ExistsInMatrix()
    {
        var known = AccessMatrix.AllRoleValues().ToHashSet();
        foreach (var group in AccessMatrix.Groups)
            foreach (var role in group.Roles)
                known.Should().Contain(role, $"group {group.Name} references unknown role {role}");
    }

    [Fact]
    public void EveryRole_IsBundledInAtLeastOneGroup()
    {
        var bundled = AccessMatrix.Groups.SelectMany(g => g.Roles).ToHashSet();
        foreach (var role in AccessMatrix.AllRoleValues())
            bundled.Should().Contain(role, $"role {role} is not assigned to any group (would be unreachable)");
    }
}
