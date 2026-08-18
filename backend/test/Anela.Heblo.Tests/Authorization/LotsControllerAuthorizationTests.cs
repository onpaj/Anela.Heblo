using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class LotsControllerAuthorizationTests
{
    [Fact]
    public void SetLabelCalibration_RequiresLabelCalibrationWrite()
    {
        var method = typeof(LotsController).GetMethod(nameof(LotsController.SetLabelCalibration));

        var attribute = method!.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            "changing the printer calibration affects every printed label and must not be " +
            "reachable with plain material-containers write access");
        attribute!.Feature.Should().Be(Feature.Manufacture_LabelCalibration);
        attribute.Level.Should().Be(AccessLevel.Write);
    }

    [Fact]
    public void GetLabelCalibration_RequiresLabelCalibrationRead()
    {
        var method = typeof(LotsController).GetMethod(nameof(LotsController.GetLabelCalibration));

        var attribute = method!.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Feature.Should().Be(Feature.Manufacture_LabelCalibration);
        attribute.Level.Should().Be(AccessLevel.Read);
    }

    [Theory]
    [InlineData(nameof(LotsController.PrintCalibrationLabel))]
    [InlineData(nameof(LotsController.FeedMedia))]
    public void MediaAlignmentActions_StayOnMaterialContainersWrite(string methodName)
    {
        var method = typeof(LotsController).GetMethod(methodName);

        var attribute = method!.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            "aligning the media is an operator action, unlike editing the calibration values");
        attribute!.Feature.Should().Be(Feature.Manufacture_MaterialContainers);
        attribute.Level.Should().Be(AccessLevel.Write);
    }
}
