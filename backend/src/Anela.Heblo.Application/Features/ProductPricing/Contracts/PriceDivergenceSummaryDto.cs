namespace Anela.Heblo.Application.Features.ProductPricing.Contracts;

/// <summary>Headline counts for the divergence report — how bad the catalogue-wide drift is.</summary>
public class PriceDivergenceSummaryDto
{
    public int TotalInScope { get; set; }
    public int InAgreementCount { get; set; }
    public int FlexiDiffersCount { get; set; }
    public int MissingInShoptetCount { get; set; }
    public int MissingInFlexiCount { get; set; }
    public int FlexiPriceTypeUnknownCount { get; set; }
    public int FlexiVatRateUnknownCount { get; set; }
}
