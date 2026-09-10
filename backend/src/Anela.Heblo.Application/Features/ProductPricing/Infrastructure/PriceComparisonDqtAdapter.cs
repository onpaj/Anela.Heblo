using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.Services;

namespace Anela.Heblo.Application.Features.ProductPricing.Infrastructure;

/// <summary>Supplies DataQuality's price check without exposing ProductPricing's internals.</summary>
public class PriceComparisonDqtAdapter : IPriceComparisonSource
{
    /// <summary>
    /// FlexiPriceTypeUnknown counts as a mismatch: Flexi's with-VAT figure was derived from an
    /// assumed price type, so any agreement it shows is untrustworthy. MissingInShoptet does
    /// not — Shoptet is the source of truth, and a product it has never priced has no
    /// comparison to fail.
    /// </summary>
    private static readonly PriceDivergenceKind[] MismatchKinds =
    {
        PriceDivergenceKind.FlexiDiffers,
        PriceDivergenceKind.MissingInFlexi,
        PriceDivergenceKind.FlexiPriceTypeUnknown,
    };

    private readonly IPriceComparisonService _comparisonService;

    public PriceComparisonDqtAdapter(IPriceComparisonService comparisonService) =>
        _comparisonService = comparisonService;

    public async Task<IReadOnlyList<PriceDivergence>> GetDivergencesAsync(CancellationToken ct)
    {
        var report = await _comparisonService.BuildReportAsync(ct);

        return report.Rows
            .Select(row => new PriceDivergence
            {
                ProductCode = row.ProductCode,
                ShoptetPriceWithVat = row.ShoptetPriceWithVat,
                FlexiPriceWithVat = row.FlexiPriceWithVat,
                Kind = row.Kind.ToString(),
                IsMismatch = MismatchKinds.Contains(row.Kind),
            })
            .ToList();
    }
}
