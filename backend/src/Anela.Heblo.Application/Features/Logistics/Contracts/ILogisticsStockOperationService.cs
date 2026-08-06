namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsStockOperationService
{
    /// <param name="persistImmediately">
    /// When true (default), the underlying StockUpOperation is committed to the database
    /// immediately. When false, it is only staged on the shared ApplicationDbContext's
    /// change tracker and flushed later by the caller's own SaveChangesAsync call, so it
    /// commits atomically together with other pending changes in the same request. Placed
    /// after CancellationToken (not before) so existing call sites that pass
    /// cancellationToken positionally as their last argument are unaffected and keep
    /// getting persistImmediately: true. Do not reorder this parameter.
    /// </param>
    Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default,
        bool persistImmediately = true);
}
