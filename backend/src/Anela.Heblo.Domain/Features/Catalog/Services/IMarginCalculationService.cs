namespace Anela.Heblo.Domain.Features.Catalog.Services;

public interface IMarginCalculationService
{
    Task<MonthlyMarginHistory> GetMarginAsync(
        CatalogAggregate product,
        IEnumerable<CatalogAggregate> allProducts,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default);
}