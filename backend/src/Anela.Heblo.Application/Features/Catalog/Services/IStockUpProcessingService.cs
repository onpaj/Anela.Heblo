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
    /// Processes all pending stock-up operations.
    /// Called by background task to submit operations to Shoptet.
    /// </summary>
    Task ProcessPendingOperationsAsync(CancellationToken ct = default);
}
