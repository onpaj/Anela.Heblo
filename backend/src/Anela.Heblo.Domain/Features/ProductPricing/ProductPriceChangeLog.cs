namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>
/// One price write made through Heblo, recorded whatever the outcome. Append-only and never
/// read to decide anything — it is history, and the only record of the partial-failure state
/// where Shoptet accepted a write and Flexi did not. Edits made directly in Shoptet's own
/// admin do not appear here; the comparison, not this log, is what catches those.
///
/// Two operations write rows: an operator's price edit (both systems written) and a
/// Shoptet -> Flexi sync (the ERP alone written, Shoptet left holding the price being
/// propagated, so <see cref="ShoptetSucceeded"/> is true without anything being sent there).
/// </summary>
public class ProductPriceChangeLog
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>
    /// The price the written system held immediately before the write — Shoptet's for a price
    /// edit, Flexi's for a sync. Null when it had none.
    /// </summary>
    public decimal? OldPriceWithVat { get; set; }
    public decimal NewPriceWithVat { get; set; }

    public DateTime ChangedAt { get; set; }
    public string ChangedBy { get; set; } = string.Empty;

    public bool ShoptetSucceeded { get; set; }
    public bool FlexiSucceeded { get; set; }

    /// <summary>Null on full success. Truncated to the column length by the repository.</summary>
    public string? ErrorMessage { get; set; }
}
