namespace Anela.Heblo.Application.Domain.Catalog.PurchaseHistory;

public interface IPurchaseHistoryClient
{
    Task<IReadOnlyList<CatalogPurchaseRecord>> GetHistoryAsync(string? productCode, DateTime dateFrom, DateTime dateTo, int limit = 0, CancellationToken cancellationToken = default);
}