using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class UserManagementControllerAuthorizationTests
{
    [Fact]
    public void GetGroupMembers_HasNoClassLevelFeatureAuthorize()
    {
        var attribute = typeof(UserManagementController)
            .GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().BeNull(
            "authorization for GetGroupMembers is intentionally placed at the method level " +
            "to allow future unauthenticated or differently-gated endpoints on the same controller; " +
            "a class-level gate would silently restrict all controller endpoints");
    }

    [Fact]
    public void GetGroupMembers_HasMethodLevelFeatureAuthorize()
    {
        var method = typeof(UserManagementController)
            .GetMethod(nameof(UserManagementController.GetGroupMembers))!;

        var attribute = method.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            "GetGroupMembers must be protected by a method-level FeatureAuthorizeAttribute; " +
            "removing it would leave the endpoint publicly accessible");
    }

    [Fact]
    public void GetGroupMembers_HasAllThreeFeatureRoles()
    {
        var method = typeof(UserManagementController)
            .GetMethod(nameof(UserManagementController.GetGroupMembers))!;

        var attribute = method.GetCustomAttribute<FeatureAuthorizeAttribute>()!;

        var roles = attribute.Roles?.Split(',') ?? [];

        roles.Should().Contain(
            AccessRoles.AdminAdministrationRead,
            "Admin_Administration holders must be able to manage group members");
        roles.Should().Contain(
            AccessRoles.ManufactureManufactureOrdersRead,
            "Manufacture_ManufactureOrders holders must be able to manage group members");
        roles.Should().Contain(
            AccessRoles.ManufactureBatchPlanningRead,
            "Manufacture_BatchPlanning holders must be able to manage group members");
    }
}
