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
    /// Shipping method GUID as returned by GET /api/orders (shipping.guid).
    /// Discover via: GET /api/eshop?include=shippingMethods
    /// Empty string means the GUID is not yet known — orders with this method are skipped.
    /// </summary>
    public string Guid { get; set; } = string.Empty;
}
