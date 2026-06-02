namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Analytics-owned read abstraction over catalog data. Implemented by the
/// Catalog module via <c>CatalogAnalyticsSourceAdapter</c> per the cross-module
/// communication pattern in <c>docs/architecture/development_guidelines.md</c>.
/// All inputs and outputs are Analytics-owned types; the adapter owns the
/// translation between <see cref="AnalyticsProductType"/> and Catalog's ProductType
/// and the projection from CatalogAggregate to <see cref="AnalyticsProduct"/>.
/// </summary>
public interface IAnalyticsProductSource
{
    /// <summary>
    /// Streams products of the requested types that have sales in the given
    /// period, projected to <see cref="AnalyticsProduct"/>. Items are yielded
    /// one by one; the underlying call still materialises a list internally,
    /// but the iteration boundary preserves the surface that callers rely on.
    /// </summary>
    IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single product projected to <see cref="AnalyticsProduct"/>,
    /// or <c>null</c> if the product is unknown. Matches the soft-fallback
    /// semantics of <c>ICatalogRepository.GetByIdAsync</c>.
    /// </summary>
    Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);
}
