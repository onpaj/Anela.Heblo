using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class CatalogMappingProfileStockPriceTests
{
    private static IMapper CreateMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<CatalogMappingProfile>(), NullLoggerFactory.Instance)
            .CreateMapper();

    [Fact]
    public void Map_CatalogAggregate_To_CatalogItemDto_ExposesStockPriceNextToPurchasePrice()
    {
        var mapper = CreateMapper();
        var aggregate = new CatalogAggregate
        {
            ProductCode = "DEZ001100",
            Stock = new StockData { Erp = 40, StockPrice = 62.35m },
            ErpPrice = new ProductPriceErp { ProductCode = "DEZ001100", PurchasePrice = 41.93m },
        };

        var dto = mapper.Map<CatalogItemDto>(aggregate);

        dto.Price!.StockPrice.Should().Be(62.35m);
        dto.Price.ErpPrice!.PurchasePrice.Should().Be(41.93m);
    }

    [Fact]
    public void Map_CatalogAggregate_WithoutStockPrice_LeavesDtoStockPriceNull()
    {
        var mapper = CreateMapper();
        var aggregate = new CatalogAggregate { ProductCode = "P1" };

        var dto = mapper.Map<CatalogItemDto>(aggregate);

        dto.Price!.StockPrice.Should().BeNull();
    }
}
