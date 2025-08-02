using Anela.Heblo.Adapters.Flexi.Common;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;

namespace Anela.Heblo.Adapters.Flexi.Purchase;

public class PurchaseHistoryFlexiDtoProfile : BaseFlexiProfile
{
    public PurchaseHistoryFlexiDtoProfile()
    {
        CreateMap<PurchaseHistoryFlexiDto, CatalogPurchaseRecord>()
            .ForMember(dest => dest.SupplierName, opt => opt.MapFrom(src => src.CompanyName))
            .ForMember(dest => dest.SupplierId, opt => opt.MapFrom(src => src.CompanyId))
            .ForMember(dest => dest.PricePerPiece, opt => opt.MapFrom(src => src.Price))
            .ForMember(dest => dest.PriceTotal, opt => opt.MapFrom(src => src.Price * (decimal)src.Amount))
            .ForMember(dest => dest.DocumentNumber, opt => opt.MapFrom(src => src.PurchaseDocumentNo));
        // DateTime conversion handled automatically by BaseFlexiProfile
    }
}