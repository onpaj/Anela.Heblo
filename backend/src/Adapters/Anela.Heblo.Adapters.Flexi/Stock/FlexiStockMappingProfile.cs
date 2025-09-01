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
            .ForMember(dest => dest.Stock, opt => opt.MapFrom(src => (decimal)src.OnStock))
            .ForMember(dest => dest.MOQ, opt => opt.MapFrom(src => src.MoqName));
    }
}