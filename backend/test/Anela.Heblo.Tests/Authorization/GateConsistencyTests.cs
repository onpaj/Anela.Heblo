using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GateConsistencyTests
{
    private static IEnumerable<Type> AllControllers()
        => typeof(Anela.Heblo.API.Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    private static IEnumerable<(MemberInfo Owner, AuthorizeAttribute Auth, GateOnAttribute? Gate)> GatedMembers(Type controller)
    {
        var classGate = controller.GetCustomAttribute<GateOnAttribute>();
        foreach (var classAuth in controller.GetCustomAttributes<AuthorizeAttribute>())
            yield return (controller, classAuth, classGate);
        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var methodGate = method.GetCustomAttribute<GateOnAttribute>();
            foreach (var methodAuth in method.GetCustomAttributes<AuthorizeAttribute>())
                yield return (method, methodAuth, methodGate ?? classGate);
        }
    }

    [Fact]
    public void EveryAuthorizeRole_MatchesGateOn()
    {
        var problems = new List<string>();
        foreach (var ctl in AllControllers())
        {
            foreach (var (owner, auth, gate) in GatedMembers(ctl))
            {
                foreach (var role in (auth.Roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = role.Trim();
                    if (trimmed == "heblo_user" || trimmed == "super_user") continue;
                    if (!PermissionString.TryParse(trimmed, out var feature, out _))
                    {
                        problems.Add($"{ctl.Name}.{owner.Name}: role '{trimmed}' is not a matrix permission");
                        continue;
                    }
                    if (gate is null)
                    {
                        problems.Add($"{ctl.Name}.{owner.Name}: [Authorize(Roles={trimmed})] without [GateOn]");
                        continue;
                    }
                    if (gate.Feature != feature)
                        problems.Add($"{ctl.Name}.{owner.Name}: [Authorize(Roles={trimmed})] but [GateOn(Feature.{gate.Feature})]");
                }
            }
        }
        problems.Should().BeEmpty();
    }

    [Fact]
    public void EveryGatedEndpoint_HasGateOn()
    {
        var problems = new List<string>();
        foreach (var ctl in AllControllers())
        {
            var classGate = ctl.GetCustomAttribute<GateOnAttribute>();
            var classAuth = ctl.GetCustomAttributes<AuthorizeAttribute>().Any();
            if (classAuth && classGate is null)
                problems.Add($"{ctl.Name}: class has [Authorize] but no [GateOn]");

            foreach (var method in ctl.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var hasAuth = method.GetCustomAttributes<AuthorizeAttribute>().Any();
                var allowAnon = method.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
                if (!hasAuth || allowAnon) continue;
                var methodGate = method.GetCustomAttribute<GateOnAttribute>();
                if (methodGate is null && classGate is null)
                    problems.Add($"{ctl.Name}.{method.Name}: [Authorize] without [GateOn] (class or method)");
            }
        }
        problems.Should().BeEmpty();
    }

    [Fact]
    public void EveryMenuPath_FeatureHasController()
    {
        var featuresWithControllers = AllControllers()
            .SelectMany(c => new[] { c.GetCustomAttribute<GateOnAttribute>() }
                .Concat(c.GetMethods().Select(m => m.GetCustomAttribute<GateOnAttribute>())))
            .Where(g => g is not null)
            .Select(g => g!.Feature)
            .ToHashSet();

        var problems = new List<string>();
        foreach (var menu in AccessMatrix.MenuPaths)
        {
            if (menu.Key.StartsWith("#")) continue; // virtual external item, no controller
            foreach (var req in menu.Requires)
                if (!featuresWithControllers.Contains(req.Feature))
                    problems.Add($"MenuPath '{menu.Key}' requires {req.Feature} but no controller is gated on it");
        }
        problems.Should().BeEmpty();
    }
}
