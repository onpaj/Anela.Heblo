using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.ShoptetOrders;

/// <summary>
/// Loads a single eshop order enriched with cooling status and product images,
/// ready for the Balení packing screen.
/// </summary>
public interface IPackingOrderClient
{
    /// <summary>
    /// Returns the packing view of the order, or null if no order exists for the code.
    /// </summary>
    Task<PackingOrder?> GetPackingOrderAsync(string code, CancellationToken ct = default);
}

/// <summary>Packing view of an eshop order. Internal contract — not an API DTO.</summary>
public class PackingOrder
{
    public string Code { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ShippingMethodName { get; set; } = string.Empty;
    public Cooling Cooling { get; set; } = Cooling.None;
    public bool IsCooled { get; set; }
    public List<PackingOrderItem> Items { get; set; } = new();
}

/// <summary>A single line on the packing screen. Also serialized in the API response.</summary>
public class PackingOrderItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>Product image URL from the catalog; null when unavailable.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Parent set name when this item is a product-set component; null otherwise.</summary>
    public string? SetName { get; set; }
}
