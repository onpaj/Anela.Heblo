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

    [FeatureAuthorize(Feature.Jobs_Trigger, Feature.Jobs_Disable, Feature.Admin_Administration)]
    private class SampleMultiFeatureController { }

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
        attr.Feature.Should().Be(Feature.Products_Catalog);
        attr.Level.Should().Be(AccessLevel.Write);
        attr.Roles.Should().Be(AccessRoles.ProductsCatalogWrite);
    }

    [Fact]
    public void FeatureAuthorize_JoinsReadRolesWithOrSemantics_ForMultipleFeatures()
    {
        var attr = typeof(SampleMultiFeatureController).GetCustomAttribute<FeatureAuthorizeAttribute>()!;
        // Comma-separated roles are evaluated as OR by ASP.NET role authorization
        attr.Roles.Should().Be(
            $"{AccessRoles.JobsTriggerRead},{AccessRoles.JobsDisableRead},{AccessRoles.AdminAdministrationRead}");
        // The primary feature is preserved for menu-path coverage checks
        attr.Feature.Should().Be(Feature.Jobs_Trigger);
    }
}
