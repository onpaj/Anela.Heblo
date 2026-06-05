using Anela.Heblo.Domain.Shared;

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

    /// <summary>Shoptet order status ID, used to verify the order is in the packing state.</summary>
    public int StatusId { get; set; }

    /// <summary>Customer remark entered at checkout; null when none.</summary>
    public string? CustomerNote { get; set; }

    /// <summary>Internal staff remark on the order; null when none.</summary>
    public string? EshopNote { get; set; }

    /// <summary>Combined street and house number from the delivery address; null when unavailable.</summary>
    public string? ShippingStreet { get; set; }

    /// <summary>Delivery address city; null when unavailable.</summary>
    public string? ShippingCity { get; set; }

    /// <summary>Delivery address ZIP/postal code; null when unavailable.</summary>
    public string? ShippingZip { get; set; }

    public List<PackingOrderItem> Items { get; set; } = new();
}

/// <summary>A single line on a packing order. Internal contract — not an API DTO.</summary>
public class PackingOrderItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>Product image URL from the catalog; null when unavailable.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Parent set name when this item is a product-set component; null otherwise.</summary>
    public string? SetName { get; set; }

    public int WeightGrams { get; set; }
}
