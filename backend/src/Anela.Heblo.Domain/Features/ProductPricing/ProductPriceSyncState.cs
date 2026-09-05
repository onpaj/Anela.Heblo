namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>Sync state of one product against one external system.</summary>
public class ProductPriceSyncState
{
    public string ProductCode { get; set; } = string.Empty;

    public PriceSyncTarget Target { get; set; }

    /// <summary>
    /// The value Heblo last successfully pushed. Null until the first push.
    /// This is what makes drift attributable — see <see cref="PriceSyncDecider"/>.
    /// </summary>
    public decimal? LastPushedPriceWithVat { get; set; }

    public DateTime? LastPushedAt { get; set; }

    public PriceSyncStatus Status { get; set; } = PriceSyncStatus.Pending;

    /// <summary>The downstream value that caused the conflict. Null unless Status is Conflict.</summary>
    public decimal? RemoteValueAtConflict { get; set; }

    public DateTime? ConflictDetectedAt { get; set; }

    public string? LastError { get; set; }
}
