using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public sealed class BundleProductRuleTests
{
    [Theory]
    [InlineData("BAL001", true)]
    [InlineData("SET042", true)]
    [InlineData("KRM001", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBundleCode_RecognizesBalAndSetPrefixes(string? code, bool expected)
    {
        // Act
        var result = BundleProductRule.IsBundleCode(code);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Resolve_PromotesProductWithBundleCodeToSet()
    {
        // Arrange
        var erpType = ProductType.Product;

        // Act
        var result = BundleProductRule.Resolve(erpType, "BAL001");

        // Assert
        result.Should().Be(ProductType.Set);
    }

    [Fact]
    public void Resolve_LeavesNonProductTypesUntouchedEvenWithBundleCode()
    {
        // Arrange
        var erpType = ProductType.Material;

        // Act
        var result = BundleProductRule.Resolve(erpType, "BAL001");

        // Assert
        result.Should().Be(ProductType.Material);
    }

    [Fact]
    public void Resolve_LeavesOrdinaryProductAsProduct()
    {
        // Act
        var result = BundleProductRule.Resolve(ProductType.Product, "KRM001");

        // Assert
        result.Should().Be(ProductType.Product);
    }
}
