namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Analytics-owned classification of products that margin reporting cares about.
/// Mirrors the subset of <c>Anela.Heblo.Domain.Features.Catalog.ProductType</c>
/// that Analytics consumes today (Product, Goods). If Analytics ever needs
/// another value, mirror it here and update the AnalyticsProductType ->
/// ProductType translation in CatalogAnalyticsSourceAdapter.
/// </summary>
public enum AnalyticsProductType
{
    Product,
    Goods,
}
