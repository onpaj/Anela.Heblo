using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
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

    [Fact]
    public void EveryMenuPath_PermissionsResolveToKnownRoles()
    {
        var defs = AccessMatrix.Features.ToDictionary(f => f.Key);
        var problems = new List<string>();

        foreach (var menu in AccessMatrix.MenuPaths)
        foreach (var req in menu.Requires)
        {
            if (!defs.TryGetValue(req.Feature, out var def))
            {
                problems.Add($"MenuPath '{menu.Key}' references unknown feature {req.Feature}");
                continue;
            }
            var ok = req.Level switch
            {
                AccessLevel.Read => true,
                AccessLevel.Write => def.HasWrite,
                AccessLevel.Admin => def.HasAdmin,
                _ => false,
            };
            if (!ok)
                problems.Add($"MenuPath '{menu.Key}' requires {req.Feature}.{req.Level} but feature does not support that level");
        }
        problems.Should().BeEmpty();
    }
}

public class ControllerAuthorizationCoverageTests
{
    private static readonly HashSet<string> KnownRoles = AccessMatrix.AllRoleValues()
        .Append(AccessRoles.Base)
        .ToHashSet();

    [Fact]
    public void AllControllerRoles_AreKnownMatrixRoles()
    {
        var apiAssembly = typeof(Anela.Heblo.API.Program).Assembly;
        var controllers = apiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var controller in controllers)
        {
            foreach (var attr in controller.GetCustomAttributes<AuthorizeAttribute>(true))
            {
                foreach (var role in (attr.Roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    KnownRoles.Should().Contain(role.Trim(),
                        $"{controller.Name} uses unknown role '{role.Trim()}'");
                }
            }

            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var attr in method.GetCustomAttributes<AuthorizeAttribute>())
                {
                    foreach (var role in (attr.Roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        KnownRoles.Should().Contain(role.Trim(),
                            $"{controller.Name}.{method.Name} uses unknown role '{role.Trim()}'");
                    }
                }
            }
        }
    }
}
