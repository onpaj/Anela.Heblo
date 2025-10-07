namespace Anela.Heblo.Domain.Features.Catalog.Services;

public interface IMarginCalculationService
{
    Task<MonthlyMarginHistory> GetMarginAsync(
        CatalogAggregate product,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default);
}