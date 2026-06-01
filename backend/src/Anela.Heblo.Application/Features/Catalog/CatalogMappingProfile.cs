using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingHistory;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperations;
using Anela.Heblo.Application.Features.Catalog.UseCases.UpdateManufactureDifficulty;
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
            .ForMember(dest => dest.Price, opt => opt.MapFrom(src => src))
            .ForMember(dest => dest.LastStockTaking, opt => opt.MapFrom(src => src.LastStockTaking))
            .ForMember(dest => dest.HasLots, opt => opt.MapFrom(src => src.HasLots))
            .ForMember(dest => dest.Lots, opt => opt.Ignore()); // Set manually in handler

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

        // ManufactureDifficulty mappings
        CreateMap<ManufactureDifficultySetting, ManufactureDifficultySettingDto>()
            .ForMember(dest => dest.IsCurrent, opt => opt.Ignore()); // Set manually in handlers

        CreateMap<CreateManufactureDifficultyRequest, ManufactureDifficultySetting>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore());

        CreateMap<UpdateManufactureDifficultyRequest, ManufactureDifficultySetting>()
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.ProductCode, opt => opt.Ignore());

        // Stock taking history mapping
        CreateMap<StockTakingRecord, StockTakingHistoryItemDto>();

        // Stock taking result mapping
        CreateMap<StockTakingRecord, StockTakingResultDto>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id.ToString()))
            .ForMember(dest => dest.Type, opt => opt.MapFrom(src => src.Type.ToString()))
            .ForMember(dest => dest.User, opt => opt.MapFrom(src => src.User ?? string.Empty))
            .ForMember(dest => dest.Error, opt => opt.MapFrom(src => src.Error ?? string.Empty));

        // Stock up operation mapping
        CreateMap<StockUpOperation, StockUpOperationDto>();
    }
}