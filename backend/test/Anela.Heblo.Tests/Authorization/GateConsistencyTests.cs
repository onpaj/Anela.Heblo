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

    [Fact]
    public void EveryGatedEndpoint_HasFeatureAuthorize()
    {
        var problems = new List<string>();
        foreach (var ctl in AllControllers())
        {
            var classHasFeatureAuth = ctl.GetCustomAttribute<FeatureAuthorizeAttribute>() is not null;

            foreach (var method in ctl.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<AllowAnonymousAttribute>() is not null) continue;

                var authorizeAttrs = method.GetCustomAttributes<AuthorizeAttribute>().ToList();
                if (authorizeAttrs.Count == 0 && !classHasFeatureAuth) continue; // not gated at all

                // Exempt: policy-based or scheme-based auth (not role-based)
                bool IsNonRoleAuth(AuthorizeAttribute a) =>
                    !string.IsNullOrEmpty(a.Policy) || !string.IsNullOrEmpty(a.AuthenticationSchemes);

                var hasFeatureAuth = method.GetCustomAttribute<FeatureAuthorizeAttribute>() is not null
                                     || classHasFeatureAuth;
                var allNonRole = authorizeAttrs.Any() && authorizeAttrs.All(IsNonRoleAuth);

                if (!hasFeatureAuth && !allNonRole)
                    problems.Add($"{ctl.Name}.{method.Name}: role-gated endpoint without [FeatureAuthorize]");
            }
        }
        problems.Should().BeEmpty();
    }

    [Fact]
    public void EveryMenuPath_FeatureHasController()
    {
        var featuresWithControllers = AllControllers()
            .SelectMany(c => new[] { c.GetCustomAttribute<FeatureAuthorizeAttribute>() }
                .Concat(c.GetMethods().Select(m => m.GetCustomAttribute<FeatureAuthorizeAttribute>())))
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
