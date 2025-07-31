using Anela.Heblo.Application.Domain.Catalog.Sales;
using AutoMapper;

namespace Anela.Heblo.Adapters.Flexi.Sales;

public class CatalogSalesFlexiDtoProfile : Profile
{
    public CatalogSalesFlexiDtoProfile()
    {
        CreateMap<CatalogSalesFlexiDto, CatalogSaleRecord>()
            .ForMember(dest => dest.Date, opt => opt.MapFrom(src => 
                DateTime.SpecifyKind(src.Date, DateTimeKind.Local)));
    }
}