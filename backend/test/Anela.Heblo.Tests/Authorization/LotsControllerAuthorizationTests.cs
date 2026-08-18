using System.Reflection;
using System.Security.Claims;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class LotsControllerAuthorizationTests
{
    private static FeatureAuthorizeAttribute SingleAttributeOn(string methodName)
    {
        var method = typeof(LotsController).GetMethod(methodName);
        // GetCustomAttributes (plural): FeatureAuthorizeAttribute is AllowMultiple, so the
        // singular overload would throw AmbiguousMatchException if a second one is added.
        var attributes = method!.GetCustomAttributes<FeatureAuthorizeAttribute>().ToList();

        attributes.Should().HaveCount(1);
        return attributes.Single();
    }

    [Fact]
    public void SetLabelCalibration_RequiresLabelCalibrationWrite()
    {
        var attribute = SingleAttributeOn(nameof(LotsController.SetLabelCalibration));

        attribute.Feature.Should().Be(
            Feature.Manufacture_LabelCalibration,
            "changing the printer calibration affects every printed label and must not be " +
            "reachable with plain material-containers write access");
        attribute.Level.Should().Be(AccessLevel.Write);
    }

    [Fact]
    public void GetLabelCalibration_RequiresLabelCalibrationRead()
    {
        var attribute = SingleAttributeOn(nameof(LotsController.GetLabelCalibration));

        attribute.Feature.Should().Be(Feature.Manufacture_LabelCalibration);
        attribute.Level.Should().Be(AccessLevel.Read);
    }

    [Theory]
    [InlineData(nameof(LotsController.PrintCalibrationLabel))]
    [InlineData(nameof(LotsController.FeedMedia))]
    public void MediaAlignmentActions_StayOnMaterialContainersWrite(string methodName)
    {
        var attribute = SingleAttributeOn(methodName);

        attribute.Feature.Should().Be(
            Feature.Manufacture_MaterialContainers,
            "aligning the media is an operator action, unlike editing the calibration values");
        attribute.Level.Should().Be(AccessLevel.Write);
    }
}

/// <summary>
/// Evaluates the authorization policy ASP.NET actually builds for the calibration
/// endpoints — the combined class-level and method-level attributes — against principals
/// holding a specific permission set. The attribute assertions above only prove the
/// attributes are present; these prove the resulting gate lets the right callers through.
/// </summary>
public class LotsControllerAuthorizationPolicyTests : IClassFixture<HebloWebApplicationFactory>
{
    private readonly HebloWebApplicationFactory _factory;

    public LotsControllerAuthorizationPolicyTests(HebloWebApplicationFactory factory) => _factory = factory;

    private const string MaterialContainersRead = "manufacture.material_containers.read";
    private const string MaterialContainersWrite = "manufacture.material_containers.write";
    private const string LabelCalibrationRead = "manufacture.label_calibration.read";
    private const string LabelCalibrationWrite = "manufacture.label_calibration.write";

    private static ClaimsPrincipal PrincipalWith(params string[] permissions)
    {
        var claims = permissions
            .Append(AccessMatrix.BaseRole)
            .Select(p => new Claim(ClaimTypes.Role, p));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private async Task<bool> IsAllowedAsync(string actionName, ClaimsPrincipal user)
    {
        // Start the host so the endpoint graph (and therefore the real authorization
        // metadata for the action) is built exactly as it is at runtime.
        _ = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        var endpoint = services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e =>
            {
                var descriptor = e.Metadata.GetMetadata<ControllerActionDescriptor>();
                return descriptor?.ControllerTypeInfo.AsType() == typeof(LotsController)
                       && descriptor.ActionName == actionName;
            });

        var policyProvider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await AuthorizationPolicy.CombineAsync(
            policyProvider,
            endpoint.Metadata.OfType<IAuthorizeData>());

        policy.Should().NotBeNull("the endpoint must be gated at all");

        var authorizationService = services.GetRequiredService<IAuthorizationService>();
        var result = await authorizationService.AuthorizeAsync(user, new DefaultHttpContext(), policy!.Requirements);
        return result.Succeeded;
    }

    [Fact]
    public async Task SetLabelCalibration_IsDenied_ForMaterialContainersWriteOnly()
    {
        var allowed = await IsAllowedAsync(
            nameof(LotsController.SetLabelCalibration),
            PrincipalWith(MaterialContainersRead, MaterialContainersWrite));

        allowed.Should().BeFalse(
            "an operator with material-containers write must not be able to change the " +
            "calibration that every printed label depends on");
    }

    [Fact]
    public async Task SetLabelCalibration_IsAllowed_ForLabelCalibrationWrite()
    {
        var allowed = await IsAllowedAsync(
            nameof(LotsController.SetLabelCalibration),
            PrincipalWith(MaterialContainersRead, LabelCalibrationWrite));

        allowed.Should().BeTrue();
    }

    [Fact]
    public async Task SetLabelCalibration_IsDenied_WithoutTheClassLevelMaterialContainersRead()
    {
        // The class-level and method-level attributes are ANDed: the calibration write
        // alone is not enough to reach an endpoint on this controller.
        var allowed = await IsAllowedAsync(
            nameof(LotsController.SetLabelCalibration),
            PrincipalWith(LabelCalibrationWrite));

        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task GetLabelCalibration_IsDenied_ForMaterialContainersOnly()
    {
        var allowed = await IsAllowedAsync(
            nameof(LotsController.GetLabelCalibration),
            PrincipalWith(MaterialContainersRead, MaterialContainersWrite));

        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task GetLabelCalibration_IsAllowed_ForLabelCalibrationRead()
    {
        var allowed = await IsAllowedAsync(
            nameof(LotsController.GetLabelCalibration),
            PrincipalWith(MaterialContainersRead, LabelCalibrationRead));

        allowed.Should().BeTrue();
    }

    [Fact]
    public async Task PrintLabels_StaysReachable_ForOperatorsWithoutCalibrationPermissions()
    {
        // Printing reads the calibration server-side, so operators must keep working
        // without any label-calibration permission.
        var allowed = await IsAllowedAsync(
            nameof(LotsController.PrintLabels),
            PrincipalWith(MaterialContainersRead, MaterialContainersWrite));

        allowed.Should().BeTrue();
    }
}
