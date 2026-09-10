using System.Globalization;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

/// <summary>
/// Shoptet-vs-Flexi retail price drift, as a DQT check.
///
/// A price comparison is a snapshot, not a date-ranged query, so the from/to bounds the
/// framework supplies are ignored — the same accommodation the other drift comparers make.
/// <c>DqtDriftResult</c>'s columns are named for the Heblo-vs-Shoptet checks that came first;
/// here <c>ShoptetValue</c> carries the Shoptet price and <c>HebloValue</c> the Flexi price.
/// </summary>
public class PriceComparisonDqtComparer : IDriftDqtComparer
{
    private readonly IPriceComparisonSource _source;

    public PriceComparisonDqtComparer(IPriceComparisonSource source) => _source = source;

    public DqtTestType TestType => DqtTestType.PriceComparison;

    public async Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var divergences = await _source.GetDivergencesAsync(ct);

        var mismatches = divergences
            .Where(d => d.IsMismatch)
            .Select(d => new DriftMismatch
            {
                EntityKey = d.ProductCode,
                MismatchCode = (int)MapMismatch(d.Kind),
                ShoptetValue = Format(d.ShoptetPriceWithVat),
                HebloValue = Format(d.FlexiPriceWithVat),
                Details = d.Kind,
            })
            .ToList();

        return new DriftComparisonResult { Mismatches = mismatches, TotalChecked = divergences.Count };
    }

    /// <summary>
    /// Maps ProductPricing's classification name onto this check's own mismatch enum. The
    /// string crosses the module boundary so DataQuality never depends on ProductPricing's
    /// enum; the mapping back to an int lives here because MismatchCode is this module's
    /// vocabulary, exactly as the sibling drift comparers do it.
    /// </summary>
    private static PriceComparisonMismatch MapMismatch(string kind) => kind switch
    {
        "FlexiDiffers" => PriceComparisonMismatch.PriceDiffers,
        "MissingInFlexi" => PriceComparisonMismatch.MissingInFlexi,
        "FlexiPriceTypeUnknown" => PriceComparisonMismatch.FlexiPriceTypeUnknown,
        _ => PriceComparisonMismatch.Unknown,
    };

    private static string? Format(decimal? value) =>
        value?.ToString("F2", CultureInfo.InvariantCulture);
}
