using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class DepartmentsControllerAuthorizationTests
{
    [Fact]
    public void Controller_IsGatedByFeatureAuthorize()
    {
        var attribute = typeof(DepartmentsController).GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            "the department list exposes internal FlexiBee accounting structure and " +
            "must not be reachable without authentication");
        attribute!.Roles.Should().Contain(AccessRoles.FinanceFinancialOverviewRead);
        attribute.Roles.Should().Contain(AccessRoles.PurchaseInvoiceClassificationRead);
    }

    [Fact]
    public void GetDepartments_DoesNotAllowAnonymous()
    {
        var method = typeof(DepartmentsController).GetMethod(nameof(DepartmentsController.GetDepartments));

        method!.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }
}
