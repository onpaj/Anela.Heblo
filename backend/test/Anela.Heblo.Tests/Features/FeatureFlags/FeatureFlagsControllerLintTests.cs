using System.Reflection;
using Anela.Heblo.API.Controllers;
using FluentAssertions;
using Microsoft.FeatureManagement.Mvc;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags;

public class FeatureFlagsControllerLintTests
{
    [Fact]
    public void FeatureFlagsController_AdminActions_MustNotHaveFeatureGateAttribute()
    {
        var controllerType = typeof(FeatureFlagsController);

        var adminActions = controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name.Contains("HttpPut")
                || a.GetType().Name.Contains("HttpDelete")
                || (a.GetType().Name.Contains("HttpGet") && m.Name.Contains("Admin"))))
            .ToList();

        adminActions.Should().NotBeEmpty(because: "controller must have admin action methods");

        foreach (var action in adminActions)
        {
            action.GetCustomAttribute<FeatureGateAttribute>()
                .Should().BeNull(because: $"{action.Name} must never be gated by a feature flag (lockout protection)");
        }

        controllerType.GetCustomAttribute<FeatureGateAttribute>()
            .Should().BeNull(because: "FeatureFlagsController class must never be gated by a feature flag");
    }
}
