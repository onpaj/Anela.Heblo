using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class WeatherForecastControllerAuthorizationTests
{
    [Fact]
    public void WeatherForecastController_IsNotGatedByFeatureAuthorize()
    {
        var attribute = typeof(WeatherForecastController).GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().BeNull(
            "the weather forecast is informational and must be available to every authenticated user; " +
            "it must not require admin.administration.read on the expedition settings page");
    }
}
