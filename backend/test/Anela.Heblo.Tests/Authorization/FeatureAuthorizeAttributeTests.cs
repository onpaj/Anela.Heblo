using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using System.Reflection;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class FeatureAuthorizeAttributeTests
{
    [FeatureAuthorize(Feature.Manufacture_BatchPlanning)]
    private class SampleReadController { }

    [FeatureAuthorize(Feature.Products_Catalog, AccessLevel.Write)]
    private class SampleWriteController { }

    [Fact]
    public void FeatureAuthorize_SetsRolesFromFeatureAndLevel()
    {
        var attr = typeof(SampleReadController).GetCustomAttribute<FeatureAuthorizeAttribute>()!;
        attr.Feature.Should().Be(Feature.Manufacture_BatchPlanning);
        attr.Level.Should().Be(AccessLevel.Read);
        attr.Roles.Should().Be(AccessRoles.ManufactureBatchPlanningRead);
    }

    [Fact]
    public void FeatureAuthorize_SetsWriteRole_WhenLevelIsWrite()
    {
        var attr = typeof(SampleWriteController).GetCustomAttribute<FeatureAuthorizeAttribute>()!;
        attr.Roles.Should().Be(AccessRoles.ProductsCatalogWrite);
    }
}
