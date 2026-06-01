using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ErpManufactureTypeTests
{
    [Fact]
    public void ErpManufactureType_ShouldHaveCorrectValues()
    {
        // Arrange & Act & Assert
        ErpManufactureType.SemiProduct.Should().Be(ErpManufactureType.SemiProduct);
        ErpManufactureType.Product.Should().Be(ErpManufactureType.Product);
    }

    [Theory]
    [InlineData(ErpManufactureType.SemiProduct)]
    [InlineData(ErpManufactureType.Product)]
    public void ErpManufactureType_AllValuesShouldBeValid(ErpManufactureType type)
    {
        // Arrange & Act & Assert
        Enum.IsDefined(typeof(ErpManufactureType), type).Should().BeTrue();
    }
}