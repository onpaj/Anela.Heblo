using Anela.Heblo.Adapters.Flexi.Common;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;
using Rem.FlexiBeeSDK.Model.Products;
using Rem.FlexiBeeSDK.Model.Products.StockToDate;

namespace Anela.Heblo.Adapters.Flexi.Stock;

public class FlexiStockMappingProfile : BaseFlexiProfile
{
    public FlexiStockMappingProfile()
    {
        CreateMap<StockToDateSummary, ErpStock>()
            // FlexiBee returns product codes and names with stray surrounding whitespace, which would
            // otherwise leak into catalog joins and the UI.
            .ForMember(dest => dest.ProductCode, opt => opt.MapFrom(src => (src.ProductCode ?? string.Empty).Trim()))
            .ForMember(dest => dest.ProductName, opt => opt.MapFrom(src => (src.ProductName ?? string.Empty).Trim()))
            .ForMember(dest => dest.Stock, opt => opt.MapFrom(src => (decimal)src.OnStock))
            // stav-skladu-k-datu rounds prumCena to 2 decimals (AKL097: 0.31 vs 0.311901), which is
            // up to ~20 % off for cheap per-gram materials. ExactAveragePrice is tuz / stavMJ, falling
            // back to prumCena when quantity or value is not positive.
            .ForMember(dest => dest.Price, opt => opt.MapFrom(src => (decimal)src.ExactAveragePrice))
            .ForMember(dest => dest.MOQ, opt => opt.MapFrom(src => src.MoqName))
            .ForMember(dest => dest.SupplierCode, opt => opt.MapFrom(src => src.SupplierCode))
            .ForMember(dest => dest.SupplierName, opt => opt.MapFrom(src => src.SupplierName));
    }
}