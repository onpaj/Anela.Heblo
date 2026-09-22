using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SyncProductPrices;

/// <summary>
/// Carries rows only, no summary: the caller already holds the whole report and merges these
/// freshly-read rows into it, so a summary computed over this subset would describe neither
/// the selection the caller sees nor the catalogue the tiles count.
/// </summary>
public class SyncProductPricesResponse : BaseResponse
{
    public List<PriceDivergenceRowDto> Rows { get; set; } = new();

    /// <summary>How many Flexi prices the sync overwrote with the Shoptet price.</summary>
    public int WrittenCount { get; set; }

    /// <summary>
    /// How many rows were written to and did not take it. Counted rather than raised as an
    /// error code: the rest of the selection was still synced, and each failed row is visible
    /// on the screen as the divergence it still is.
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// How many rows needed a write and were never attempted, because the run spent its write
    /// budget first. Non-zero means "run it again", not "something went wrong".
    /// </summary>
    public int RemainingCount { get; set; }
}
