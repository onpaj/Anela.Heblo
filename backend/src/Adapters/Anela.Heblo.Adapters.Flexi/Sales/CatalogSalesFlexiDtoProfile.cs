using Anela.Heblo.Catalog.Sales;
using AutoMapper;

namespace Anela.Heblo.Adapters.Flexi.Sales;

public class CatalogSalesFlexiDtoProfile : Profile
{
    public CatalogSalesFlexiDtoProfile()
    {
        CreateMap<CatalogSalesFlexiDto, CatalogSales>()
            ;
    }
}