using AutoMapper;
using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.Domain.Catalog.Stock;
using Anela.Heblo.Application.features.catalog.contracts;

namespace Anela.Heblo.Application.features.catalog.Application;

public class CatalogMappingProfile : Profile
{
    public CatalogMappingProfile()
    {
        CreateMap<CatalogAggregate, CatalogItemDto>();

        CreateMap<StockData, StockDto>();

        CreateMap<CatalogProperties, PropertiesDto>();
    }
}