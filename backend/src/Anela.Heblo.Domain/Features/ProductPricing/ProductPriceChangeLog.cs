namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>
/// One price edit made through Heblo, recorded whatever the outcome. Append-only and never
/// read to decide anything — it is history, and the only record of the partial-failure state
/// where Shoptet accepted a write and Flexi did not. Edits made directly in Shoptet's own
/// admin do not appear here; the comparison, not this log, is what catches those.
/// </summary>
public class ProductPriceChangeLog
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>Shoptet's price immediately before the write; null when it had none.</summary>
    public decimal? OldPriceWithVat { get; set; }
    public decimal NewPriceWithVat { get; set; }

    public DateTime ChangedAt { get; set; }
    public string ChangedBy { get; set; } = string.Empty;

    public bool ShoptetSucceeded { get; set; }
    public bool FlexiSucceeded { get; set; }

    /// <summary>Null on full success. Truncated to the column length by the repository.</summary>
    public string? ErrorMessage { get; set; }
}
