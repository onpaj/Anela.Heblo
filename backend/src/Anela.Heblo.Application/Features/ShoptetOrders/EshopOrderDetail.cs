namespace Anela.Heblo.Application.Features.ShoptetOrders;

/// <summary>
/// An order's status and lines as returned by GET /api/orders/{code}. Carries only what the
/// double-shot swap and the missing-invoice job need (docs/integrations/shoptet-api.md §3.17).
/// </summary>
public class EshopOrderDetail
{
    public string Code { get; init; } = string.Empty;
    public int StatusId { get; init; }
    /// <summary>
    /// False when the order has no billing method — Shoptet then refuses to invoice it
    /// (422 "Unknown billingMethodId provided").
    /// </summary>
    public bool HasBillingMethod { get; init; }
    public IReadOnlyList<EshopOrderLine> Lines { get; init; } = [];
}

public class EshopOrderLine
{
    public long ItemId { get; init; }
    public string ItemType { get; init; } = string.Empty;
    public string? Code { get; init; }
    public decimal Amount { get; init; }
    public decimal UnitPriceWithVat { get; init; }
    public decimal VatRate { get; init; }
    public decimal PriceRatio { get; init; } = 1m;
    public string? Remark { get; init; }
}
