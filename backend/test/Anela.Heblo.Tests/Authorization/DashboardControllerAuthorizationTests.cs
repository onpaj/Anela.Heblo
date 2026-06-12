using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class DashboardControllerAuthorizationTests
{
    [Fact]
    public void DashboardController_IsNotGatedByFeatureAuthorize()
    {
        var attribute = typeof(DashboardController).GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().BeNull(
            "the read-only dashboard must be available to every authenticated user; " +
            "per-tile access is enforced in GetTileDataHandler");
    }
}
