namespace Anela.Heblo.Application.Features.ShoptetOrders;

/// <summary>
/// A line to add to an existing order via POST /api/orders/{code}/item.
/// </summary>
public class NewEshopOrderItem
{
    public string ItemType { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal UnitPriceWithVat { get; init; }
    public decimal VatRate { get; init; }
    public decimal PriceRatio { get; init; } = 1m;
    public string? Remark { get; init; }
}
