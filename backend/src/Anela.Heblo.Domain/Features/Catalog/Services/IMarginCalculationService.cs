namespace Anela.Heblo.Domain.Features.Catalog.Services;

public interface IMarginCalculationService
{
    Task<ProductMarginResult> CalculateAllMarginLevelsAsync(
        CatalogAggregate product,
        CancellationToken cancellationToken = default);

    Task<MonthlyMarginHistory> CalculateMonthlyMarginHistoryAsync(
        CatalogAggregate product,
        int monthsBack = 13,
        CancellationToken cancellationToken = default);
}