using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class ManufactureSettingsControllerAuthorizationTests
{
    [Fact]
    public void Controller_IsGatedByFeatureAuthorize()
    {
        var attribute = typeof(ManufactureSettingsController).GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            "the settings endpoint exposes the tenant's Entra ID manufacture-group id " +
            "and must sit behind the same gate as its sibling Manufacture controllers");
        attribute!.Feature.Should().Be(Feature.Manufacture_ManufactureOrders);
    }

    [Fact]
    public void GetSettings_DoesNotAllowAnonymous()
    {
        var method = typeof(ManufactureSettingsController).GetMethod(nameof(ManufactureSettingsController.GetSettings));

        method!.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }
}
