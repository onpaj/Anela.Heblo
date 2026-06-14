namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Discriminator selecting which margin amount Analytics reports for a product
/// (<see cref="AnalyticsProduct.M0Amount"/>, <see cref="AnalyticsProduct.M1Amount"/>,
/// or <see cref="AnalyticsProduct.M2Amount"/>). Distinct from the
/// <c>Anela.Heblo.Domain.Features.Catalog.MarginLevel</c> value object,
/// which represents a computed margin result (percentage, amount, costs).
/// </summary>
public enum MarginLevel
{
    M0,
    M1,
    M2,
}
