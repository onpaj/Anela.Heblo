using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Rem.FlexiBeeSDK.Model.Products;
using Rem.FlexiBeeSDK.Model.Products.StockToDate;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Stock;

public class FlexiStockMappingProfileTests
{
    private static IMapper CreateMapper()
    {
        var config = new MapperConfiguration(
            c => c.AddProfile<FlexiStockMappingProfile>(),
            NullLoggerFactory.Instance);
        return config.CreateMapper();
    }

    [Theory]
    [InlineData("Lahvička 500ml hnědé sklo ", "Lahvička 500ml hnědé sklo")]
    [InlineData("  leading and trailing  ", "leading and trailing")]
    [InlineData("\tName with tab\t", "Name with tab")]
    [InlineData("already clean", "already clean")]
    public void Map_TrimsProductName(string sourceName, string expected)
    {
        // Arrange
        var summary = new StockToDateSummary { ProductCode = "CODE", ProductName = sourceName };

        // Act
        var result = CreateMapper().Map<ErpStock>(summary);

        // Assert
        result.ProductName.Should().Be(expected);
    }

    [Theory]
    [InlineData("CODE123 ", "CODE123")]
    [InlineData("  CODE123", "CODE123")]
    [InlineData("CODE123", "CODE123")]
    public void Map_TrimsProductCode(string sourceCode, string expected)
    {
        // Arrange
        var summary = new StockToDateSummary { ProductCode = sourceCode, ProductName = "Name" };

        // Act
        var result = CreateMapper().Map<ErpStock>(summary);

        // Assert
        result.ProductCode.Should().Be(expected);
    }
}
