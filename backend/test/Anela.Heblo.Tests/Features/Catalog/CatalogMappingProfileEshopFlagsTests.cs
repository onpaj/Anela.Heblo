using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Eshop;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class CatalogMappingProfileEshopFlagsTests
{
    private static IMapper CreateMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<CatalogMappingProfile>(), NullLoggerFactory.Instance)
            .CreateMapper();

    [Fact]
    public void GetCatalogDetail_ProductWithFlags_ReturnsFlagTitles()
    {
        var aggregate = new CatalogAggregate
        {
            ProductCode = "P1",
            EshopFlags = new[]
            {
                new EshopProductFlag { Code = "bestseller", Title = "Bestseller" },
                new EshopProductFlag { Code = "sale", Title = "Výprodej" },
            },
        };

        var dto = CreateMapper().Map<CatalogItemDto>(aggregate);

        dto.EshopFlags.Select(f => f.Title).Should().Equal("Bestseller", "Výprodej");
        dto.EshopFlags.Select(f => f.Code).Should().Equal("bestseller", "sale");
    }

    [Fact]
    public void GetCatalogDetail_ProductWithoutFlags_ReturnsEmptyList()
    {
        var dto = CreateMapper().Map<CatalogItemDto>(new CatalogAggregate { ProductCode = "P1" });

        dto.EshopFlags.Should().NotBeNull().And.BeEmpty();
    }
}
