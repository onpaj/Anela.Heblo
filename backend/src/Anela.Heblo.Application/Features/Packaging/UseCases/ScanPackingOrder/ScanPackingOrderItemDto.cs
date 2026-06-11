namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

/// <summary>Public API DTO for a single packing-order line returned by ScanPackingOrder.</summary>
public sealed class ScanPackingOrderItemDto
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>Product image URL from the catalog; null when unavailable.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Parent set name when this item is a product-set component; null otherwise.</summary>
    public string? SetName { get; set; }
}
