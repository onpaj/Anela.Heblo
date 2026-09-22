using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

/// <summary>
/// Both mutating actions on this controller write straight into the live Shoptet store and the
/// live ABRA Flexi ERP, neither of which has a test environment. The class-level gate defaults
/// to Read, so a missing method-level attribute would put a bulk price write behind a
/// read permission — and every other suite would stay green.
/// </summary>
public class ProductPricingControllerAuthorizationTests
{
    [Fact]
    public void ProductPricingController_IsGatedByProductsCatalogRead()
    {
        var attribute = typeof(ProductPricingController).GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Feature.Should().Be(Feature.Products_Catalog);
        attribute.Level.Should().Be(AccessLevel.Read);
    }

    [Theory]
    [InlineData(nameof(ProductPricingController.SetPrice))]
    [InlineData(nameof(ProductPricingController.Sync))]
    public void PriceWrites_RequireProductsCatalogWrite(string methodName)
    {
        var method = typeof(ProductPricingController).GetMethod(methodName)!;
        var attribute = method.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            $"{methodName} writes a price into a live system and must require " +
            "Products_Catalog Write, not the class-level Read default");
        attribute!.Feature.Should().Be(Feature.Products_Catalog);
        attribute.Level.Should().Be(AccessLevel.Write);
    }

    [Fact]
    public void DivergenceReport_StaysAtTheClassLevelReadGate()
    {
        var method = typeof(ProductPricingController).GetMethod(
            nameof(ProductPricingController.GetDivergenceReport))!;

        method.GetCustomAttributes<FeatureAuthorizeAttribute>(inherit: false)
            .Should()
            .BeEmpty("comparing the two systems writes nowhere; a method-level attribute would " +
                     "either broaden or narrow access silently");
    }
}
