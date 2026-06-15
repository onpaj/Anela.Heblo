using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GridLayoutsControllerAuthorizationTests
{
    [Fact]
    public void GridLayoutsController_IsNotGatedByFeatureAuthorize()
    {
        var attribute = typeof(GridLayoutsController).GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().BeNull(
            "grid column layouts are per-user UI preferences; every authenticated user " +
            "may persist their own, so the controller falls back to the default authenticated policy");
    }
}
