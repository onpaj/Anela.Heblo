using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ShippingMethod
{
    public Carriers Carrier { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int PageSize { get; set; } = 8;
}
