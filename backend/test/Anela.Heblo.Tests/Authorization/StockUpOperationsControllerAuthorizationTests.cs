using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class StockUpOperationsControllerAuthorizationTests
{
    [Fact]
    public void StockUpOperationsController_IsGatedByWarehouseStockUpRead()
    {
        var attribute = typeof(StockUpOperationsController)
            .GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            "the controller is the single source of authorization for read endpoints; " +
            "removing the class-level gate would silently broaden the API");
        attribute!.Feature.Should().Be(Feature.Warehouse_StockUp);
        attribute.Level.Should().Be(AccessLevel.Read);
    }

    [Fact]
    public void GetSummary_HasNoOverridingAuthorizeAttribute()
    {
        var method = typeof(StockUpOperationsController)
            .GetMethod(nameof(StockUpOperationsController.GetSummary))!;

        method.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            .Should()
            .BeEmpty(
                "the class-level Warehouse_StockUp Read gate is the authoritative gate " +
                "for the summary endpoint; an overriding method-level attribute would " +
                "either broaden or narrow access silently");
    }

    [Theory]
    [InlineData(nameof(StockUpOperationsController.RetryOperation))]
    [InlineData(nameof(StockUpOperationsController.AcceptOperation))]
    public void WriteActions_RemainGatedAtWriteLevel(string methodName)
    {
        var method = typeof(StockUpOperationsController).GetMethod(methodName)!;
        var attribute = method.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            $"{methodName} mutates stock-up state and must require Warehouse_StockUp Write");
        attribute!.Feature.Should().Be(Feature.Warehouse_StockUp);
        attribute.Level.Should().Be(AccessLevel.Write);
    }
}
