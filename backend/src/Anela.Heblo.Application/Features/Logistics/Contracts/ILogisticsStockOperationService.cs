namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsStockOperationService
{
    Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default);

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
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default);
}
