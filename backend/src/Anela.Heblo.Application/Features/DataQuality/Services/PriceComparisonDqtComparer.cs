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

        GuardAgainstAComparisonThatComparedNothing(divergences);

        var mismatches = divergences
            .Where(d => d.IsMismatch)
            .Select(ToDriftRow)
            .ToList();

        // Not mismatches (Shoptet is the source of truth; a product it has never priced has
        // no comparison to fail) but recorded anyway, so the count is observable on the
        // dashboard tile and in the run detail instead of vanishing entirely.
        var missingInShoptet = divergences
            .Where(d => !d.IsMismatch && d.ShoptetPriceWithVat is null)
            .Select(ToDriftRow)
            .ToList();

        return new DriftComparisonResult
        {
            Mismatches = mismatches,
            Informational = missingInShoptet,
            TotalChecked = divergences.Count,
        };
    }

    /// <summary>
    /// A price comparison in which not one product had a Shoptet price compared nothing at
    /// all, and must not be allowed to report a healthy zero.
    ///
    /// MissingInShoptet is excluded from the mismatch count on purpose, so an empty or
    /// truncated Shoptet read classifies EVERY row that way, produces zero mismatches,
    /// completes the run, and renders the tile green "vše OK". That is reachable without any
    /// exception: a wrong or emptied <c>Shoptet:DefaultPriceListId</c>, a paginator that
    /// truncates the read to the first 100 items, or every price being unreadable (which is
    /// logged, not thrown). Throwing is what makes <see cref="DriftDqtJobRunner"/> record the
    /// run Failed and the tile go red — the same path a read *failure* already takes.
    /// </summary>
    private static void GuardAgainstAComparisonThatComparedNothing(IReadOnlyList<PriceDivergence> divergences)
    {
        if (divergences.Any(d => d.ShoptetPriceWithVat is not null))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Price comparison found no Shoptet price for any of the {divergences.Count} in-scope " +
            "product(s), so nothing was actually compared. Shoptet is the retail source of truth — " +
            "this is a failed or truncated read (check Shoptet:DefaultPriceListId and the price " +
            "list paginator), not a clean comparison, and is reported as a failed run rather than " +
            "as zero mismatches.");
    }

    private static DriftMismatch ToDriftRow(PriceDivergence divergence) => new()
    {
        EntityKey = divergence.ProductCode,
        MismatchCode = (int)MapMismatch(divergence.Kind),
        ShoptetValue = Format(divergence.ShoptetPriceWithVat),
        HebloValue = Format(divergence.FlexiPriceWithVat),
        Details = divergence.Kind,
    };

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
        "FlexiVatRateUnknown" => PriceComparisonMismatch.FlexiVatRateUnknown,
        "MissingInShoptet" => PriceComparisonMismatch.MissingInShoptet,
        _ => PriceComparisonMismatch.Unknown,
    };

    private static string? Format(decimal? value) =>
        value?.ToString("F2", CultureInfo.InvariantCulture);
}
