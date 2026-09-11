namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

/// <summary>
/// Consumer-owned contract (see development_guidelines.md, ILeafletKnowledgeSource pattern):
/// DataQuality declares what it needs, ProductPricing supplies the adapter and registers the
/// binding. Only the operations this check actually consumes are exposed.
/// </summary>
public interface IPriceComparisonSource
{
    Task<IReadOnlyList<PriceDivergence>> GetDivergencesAsync(CancellationToken ct);
}

public class PriceDivergence
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal? ShoptetPriceWithVat { get; set; }
    public decimal? FlexiPriceWithVat { get; set; }

    /// <summary>The classification name, e.g. "FlexiDiffers" — carried as text so DataQuality
    /// does not depend on ProductPricing's enum.</summary>
    public string Kind { get; set; } = string.Empty;

    public bool IsMismatch { get; set; }
}
