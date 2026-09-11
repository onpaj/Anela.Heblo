namespace Anela.Heblo.Application.Features.ProductPricing.Services;

/// <summary>
/// Read-only comparison of Shoptet and Flexi retail prices. Never writes to either — see
/// <see cref="PriceComparisonService"/> for the constructor dependencies, which
/// deliberately exclude every write-capable interface member
/// (<c>IEshopPriceListClient.SetPriceWithVatAsync</c>, <c>IErpPriceWriter</c>).
/// </summary>
public interface IPriceComparisonService
{
    Task<PriceComparisonResult> BuildReportAsync(CancellationToken ct);

    /// <summary>
    /// Rebuilds the report for one selection of products, reading both sources fresh — the
    /// manual "sync" the comparison screen offers for the rows currently on display.
    /// Codes that are not priced catalog products are ignored.
    ///
    /// Flexi's ceník cache is bypassed however small the selection: the ERP has no per-product
    /// price read, so the whole ceník is re-read and the shared five-minute cache entry is
    /// repopulated for every other reader too. Intended, not incidental.
    /// </summary>
    Task<PriceComparisonResult> BuildScopedReportAsync(
        IReadOnlyCollection<string> productCodes, CancellationToken ct);
}
