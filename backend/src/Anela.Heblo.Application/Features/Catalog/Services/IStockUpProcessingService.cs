using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface IStockUpProcessingService
{
    /// <summary>
    /// Creates a new stock-up operation in Pending state.
    /// Called by handlers/services when they need to schedule a stock-up operation.
    /// </summary>
    Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default);

    /// <summary>
    /// Idempotently stages a stock-up operation in the current unit of work WITHOUT saving:
    /// no-op if a row with this DocumentNumber already exists, otherwise adds a new Pending
    /// operation to the change tracker. The caller is responsible for committing it together
    /// with its own aggregate's SaveChangesAsync so both persist atomically.
    /// </summary>
    Task StageOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default);

    /// <summary>
    /// Processes all pending stock-up operations.
    /// Called by background task to submit operations to Shoptet.
    /// </summary>
    Task ProcessPendingOperationsAsync(CancellationToken ct = default);
}
