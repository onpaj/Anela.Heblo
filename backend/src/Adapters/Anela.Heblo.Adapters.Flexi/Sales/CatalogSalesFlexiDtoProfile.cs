using Anela.Heblo.Adapters.Flexi.Common;
using Anela.Heblo.Application.Domain.Catalog.Sales;

namespace Anela.Heblo.Adapters.Flexi.Sales;

public class CatalogSalesFlexiDtoProfile : BaseFlexiProfile
{
    public CatalogSalesFlexiDtoProfile()
    {
        // DateTime conversion is handled by BaseFlexiProfile automatically
        CreateMap<CatalogSalesFlexiDto, CatalogSaleRecord>();
    }
}