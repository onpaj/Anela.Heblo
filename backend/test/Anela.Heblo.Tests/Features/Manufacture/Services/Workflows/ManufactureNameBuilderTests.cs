using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services.Workflows;

public class ManufactureNameBuilderTests
{
    private readonly Mock<IProductNameFormatter> _nameFormatterMock;
    private readonly ManufactureNameBuilder _builder;

    public ManufactureNameBuilderTests()
    {
        _nameFormatterMock = new Mock<IProductNameFormatter>();
        _nameFormatterMock
            .Setup(x => x.ShortProductName(It.IsAny<string>()))
            .Returns<string>(name => name);

        _builder = new ManufactureNameBuilder(_nameFormatterMock.Object);
    }

    [Fact]
    public void Build_WhenProductCodeShorterThan6Chars_DoesNotThrow()
    {
        // Arrange – 3-char code, shorter than the 6-char prefix
        var order = CreateOrder("ABC", "Bisabolol", new[] { ("ABC", "ABC") });

        // Act
        var act = () => _builder.Build(order, ErpManufactureType.SemiProduct);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Build_WhenSemiProduct_PrependsMSuffix()
    {
        // Arrange
        var order = CreateOrder("CODE12", "Bisabolol", new[] { ("P001", "Product 1") });

        // Act
        var result = _builder.Build(order, ErpManufactureType.SemiProduct);

        // Assert
        result.Should().StartWith("CODE12M ");
    }

    [Fact]
    public void Build_WhenSinglephaseProduct_ReturnsSemiCodeOnly()
    {
        // Arrange – all products have the same code as the semi-product
        var semiCode = "CODE12";
        var order = CreateOrder(semiCode, "Bisabolol", new[] { (semiCode, "Product") });

        // Act
        var result = _builder.Build(order, ErpManufactureType.Product);

        // Assert
        result.Should().Be(semiCode);
    }

    [Fact]
    public void Build_WhenResultExceeds40Chars_TruncatesSafely()
    {
        // Arrange – long name that will push the formatted result past 40 chars
        _nameFormatterMock
            .Setup(x => x.ShortProductName(It.IsAny<string>()))
            .Returns("VeryLongShortNameThatExceedsFortyCharactersLimit");

        var order = CreateOrder("ABCDEF", "A Very Long Semi Product Name That Will Overflow", new[] { ("P001", "Product 1") });

        // Act
        var result = _builder.Build(order, ErpManufactureType.Product);

        // Assert
        result.Length.Should().BeLessOrEqualTo(40);
    }

    [Fact]
    public void Build_WhenCodeShorterThanPrefix_UsesFullCode()
    {
        // Arrange – 2-char code, shorter than the 6-char prefix
        var order = CreateOrder("XY", "Semi Product", new[] { ("P001", "Product 1") });

        // Act
        var result = _builder.Build(order, ErpManufactureType.SemiProduct);

        // Assert
        result.Should().StartWith("XY");
    }

    private static UpdateManufactureOrderDto CreateOrder(
        string semiCode,
        string semiName,
        (string code, string name)[] products)
    {
        return new UpdateManufactureOrderDto
        {
            OrderNumber = "MO-TEST-001",
            SemiProduct = new UpdateManufactureOrderSemiProductDto
            {
                ProductCode = semiCode,
                ProductName = semiName,
                PlannedQuantity = 10m,
                ActualQuantity = 10m,
            },
            Products = products.Select(p => new UpdateManufactureOrderProductDto
            {
                ProductCode = p.code,
                ProductName = p.name,
                SemiProductCode = semiCode,
                PlannedQuantity = 10m,
                ActualQuantity = 10m,
            }).ToList(),
        };
    }
}
