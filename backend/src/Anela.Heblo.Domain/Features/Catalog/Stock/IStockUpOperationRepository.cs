namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public interface IStockUpOperationRepository
{
    Task<StockUpOperation?> GetByDocumentNumberAsync(string documentNumber, CancellationToken ct = default);
    Task<StockUpOperation?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<StockUpOperation>> GetByStateAsync(StockUpOperationState state, CancellationToken ct = default);
    Task<List<StockUpOperation>> GetFailedOperationsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get all stock-up operations for a specific source (transport box or gift package)
    /// </summary>
    Task<List<StockUpOperation>> GetBySourceAsync(
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default);

    IQueryable<StockUpOperation> GetAll();
    Task<StockUpOperation> AddAsync(StockUpOperation operation, CancellationToken ct = default);
    Task UpdateAsync(StockUpOperation operation, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
