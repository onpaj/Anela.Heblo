namespace Anela.Heblo.Application.Domain.Catalog.Sales;

public interface ICatalogSalesClient
{
    Task<IList<CatalogSaleRecord>> GetAsync(DateTime dateFrom, DateTime dateTo, int limit = 0, CancellationToken cancellationToken = default);
}