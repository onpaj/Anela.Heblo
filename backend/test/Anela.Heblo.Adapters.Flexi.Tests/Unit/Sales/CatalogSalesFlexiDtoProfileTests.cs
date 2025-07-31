using Anela.Heblo.Adapters.Flexi.Sales;
using Anela.Heblo.Application.Domain.Catalog.Sales;
using AutoMapper;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Unit.Sales;

public class CatalogSalesFlexiDtoProfileTests
{
    private readonly IMapper _mapper;

    public CatalogSalesFlexiDtoProfileTests()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<CatalogSalesFlexiDtoProfile>();
        });
        _mapper = config.CreateMapper();
    }

    [Fact]
    public void Should_Map_DateTime_With_Local_Kind()
    {
        // Arrange
        var sourceDate = new DateTime(2025, 6, 1, 10, 30, 0, DateTimeKind.Utc);
        var dto = new CatalogSalesFlexiDto
        {
            Date = sourceDate,
            ProductCode = "TEST001",
            ProductName = "Test Product",
            AmountTotal = 10.0,
            AmountB2B = 6.0,
            AmountB2C = 4.0,
            SumTotal = 100.0m,
            SumB2B = 60.0m,
            SumB2C = 40.0m
        };

        // Act
        var result = _mapper.Map<CatalogSaleRecord>(dto);

        // Assert
        result.Should().NotBeNull();
        result.Date.Kind.Should().Be(DateTimeKind.Local);
        result.Date.Should().Be(new DateTime(2025, 6, 1, 10, 30, 0));
        result.ProductCode.Should().Be("TEST001");
        result.ProductName.Should().Be("Test Product");
    }

    [Fact]
    public void Should_Map_All_Properties_Correctly()
    {
        // Arrange
        var dto = new CatalogSalesFlexiDto
        {
            Date = new DateTime(2025, 5, 25),
            ProductCode = "PROD123",
            ProductName = "Sample Product",
            AmountTotal = 25.5,
            AmountB2B = 15.5,
            AmountB2C = 10.0,
            SumTotal = 255.50m,
            SumB2B = 155.50m,
            SumB2C = 100.00m
        };

        // Act
        var result = _mapper.Map<CatalogSaleRecord>(dto);

        // Assert
        result.Should().NotBeNull();
        result.Date.Should().Be(dto.Date);
        result.ProductCode.Should().Be(dto.ProductCode);
        result.ProductName.Should().Be(dto.ProductName);
        result.AmountTotal.Should().Be(dto.AmountTotal);
        result.AmountB2B.Should().Be(dto.AmountB2B);
        result.AmountB2C.Should().Be(dto.AmountB2C);
        result.SumTotal.Should().Be(dto.SumTotal);
        result.SumB2B.Should().Be(dto.SumB2B);
        result.SumB2C.Should().Be(dto.SumB2C);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Should_Always_Map_To_Local_Kind_Regardless_Of_Source(DateTimeKind sourceKind)
    {
        // Arrange
        var sourceDate = DateTime.SpecifyKind(new DateTime(2025, 6, 1), sourceKind);
        var dto = new CatalogSalesFlexiDto
        {
            Date = sourceDate,
            ProductCode = "TEST",
            ProductName = "Test"
        };

        // Act
        var result = _mapper.Map<CatalogSaleRecord>(dto);

        // Assert
        result.Date.Kind.Should().Be(DateTimeKind.Local);
        result.Date.Ticks.Should().Be(sourceDate.Ticks);
    }
}