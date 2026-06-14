using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class PermissionStringTests
{
    [Theory]
    [InlineData(Feature.Purchase_PurchaseOrders, AccessLevel.Read, "purchase.purchase_orders.read")]
    [InlineData(Feature.Manufacture_BatchPlanning, AccessLevel.Write, "manufacture.batch_planning.write")]
    [InlineData(Feature.Marketing_Photobank, AccessLevel.Admin, "marketing.photobank.admin")]
    [InlineData(Feature.Admin_Administration, AccessLevel.Read, "admin.administration.read")]
    public void Format_BuildsWireString(Feature f, AccessLevel l, string expected)
    {
        PermissionString.Format(f, l).Should().Be(expected);
    }

    [Theory]
    [InlineData("purchase.purchase_orders.read", Feature.Purchase_PurchaseOrders, AccessLevel.Read)]
    [InlineData("manufacture.batch_planning.write", Feature.Manufacture_BatchPlanning, AccessLevel.Write)]
    [InlineData("admin.administration.read", Feature.Admin_Administration, AccessLevel.Read)]
    public void TryParse_RecognizesValidStrings(string s, Feature f, AccessLevel l)
    {
        PermissionString.TryParse(s, out var feature, out var level).Should().BeTrue();
        feature.Should().Be(f);
        level.Should().Be(l);
    }

    [Theory]
    [InlineData("heblo_user")]
    [InlineData("super_user")]
    [InlineData("nonsense")]
    [InlineData("purchase.unknown.read")]
    public void TryParse_RejectsNonMatrixStrings(string s)
    {
        PermissionString.TryParse(s, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(Feature.Purchase_PurchaseOrders, "PurchasePurchaseOrders")]
    [InlineData(Feature.Manufacture_BatchPlanning, "ManufactureBatchPlanning")]
    [InlineData(Feature.Admin_FeatureFlags, "AdminFeatureFlags")]
    public void ConstantSuffix_StripsUnderscore(Feature f, string expected)
    {
        PermissionString.ConstantSuffix(f).Should().Be(expected);
    }
}
