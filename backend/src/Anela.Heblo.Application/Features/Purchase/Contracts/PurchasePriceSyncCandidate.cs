namespace Anela.Heblo.Application.Features.Purchase.Contracts;

/// <summary>
/// A material or goods item considered by the nightly purchase price sync.
/// </summary>
public sealed class PurchasePriceSyncCandidate
{
    public required string ProductCode { get; init; }
    public required MaterialProductType ProductType { get; init; }

    /// <summary>Internal Flexi ceník id (<c>idcenik</c>); always &gt; 0 for a candidate.</summary>
    public required int ErpItemId { get; init; }

    /// <summary>Current ceník <c>nakupCena</c> (excluding VAT).</summary>
    public required decimal CurrentPurchasePrice { get; init; }

    /// <summary>Today's average stock price (<c>prumCena</c>) in the item's warehouse; null when there is no stock row.</summary>
    public decimal? StockPrice { get; init; }
}
