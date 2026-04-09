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

    /// <summary>
    /// Maximum number of items (across all orders in a batch) before starting a new batch.
    /// Controls how many orders end up in a single PDF so that the printed protocol fits on two pages.
    /// If a single order has more items than this limit it still gets its own batch.
    /// </summary>
    public int MaxItems { get; set; } = 20;

    /// <summary>
    /// Maximum number of orders per batch. Default is no limit (int.MaxValue).
    /// Use for carriers like Osobak where each order must be its own PDF regardless of item count.
    /// </summary>
    public int MaxOrders { get; set; } = int.MaxValue;

    /// <summary>
    /// Shipping method GUIDs as returned by GET /api/orders (shipping.guid).
    /// One shipping ID can map to multiple GUIDs (e.g. when Shoptet creates a new variant of the same method).
    /// Discover via: GET /api/eshop?include=shippingMethods
    /// Empty array means the GUIDs are not yet known — orders with this method are skipped.
    /// </summary>
    public string[] Guids { get; set; } = [];
}
