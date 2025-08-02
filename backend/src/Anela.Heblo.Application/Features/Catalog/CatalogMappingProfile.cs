using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Catalog;

public class CatalogMappingProfile : Profile
{
    public CatalogMappingProfile()
    {
        CreateMap<CatalogAggregate, CatalogItemDto>()
            .ForMember(dest => dest.Price, opt => opt.MapFrom(src => src));

        CreateMap<StockData, StockDto>();

        CreateMap<CatalogProperties, PropertiesDto>();

        CreateMap<CatalogAggregate, PriceDto>()
            .ForMember(dest => dest.CurrentSellingPrice, opt => opt.MapFrom(src => src.CurrentSellingPrice))
            .ForMember(dest => dest.CurrentPurchasePrice, opt => opt.MapFrom(src => src.CurrentPurchasePrice))
            .ForMember(dest => dest.SellingPriceWithVat, opt => opt.MapFrom(src => src.SellingPriceWithVat))
            .ForMember(dest => dest.PurchasePriceWithVat, opt => opt.MapFrom(src => src.PurchasePriceWithVat))
            .ForMember(dest => dest.EshopPrice, opt => opt.MapFrom(src => src.EshopPrice))
            .ForMember(dest => dest.ErpPrice, opt => opt.MapFrom(src => src.ErpPrice));

        CreateMap<ProductPriceEshop, EshopPriceDto>();

        CreateMap<ProductPriceErp, ErpPriceDto>();
    }
}