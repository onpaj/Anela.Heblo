using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ShippingMethod
{
    public Carriers Carrier { get; set; }

    /// <summary>
    /// Admin-internal numeric ID — used for Playwright URL filters (?f[shippingId]=X).
    /// NOT returned by the REST API order list endpoint.
    /// </summary>
    public int Id { get; set; }

    public string Name { get; set; } = null!;
    public int PageSize { get; set; } = 8;

    /// <summary>
    /// Shipping method GUIDs as returned by GET /api/orders (shipping.guid).
    /// One shipping ID can map to multiple GUIDs (e.g. when Shoptet creates a new variant of the same method).
    /// Discover via: GET /api/eshop?include=shippingMethods
    /// Empty array means the GUIDs are not yet known — orders with this method are skipped.
    /// </summary>
    public string[] Guids { get; set; } = [];
}
