using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface IStockUpProcessingService
{
    /// <summary>
    /// Creates a new stock-up operation in Pending state.
    /// Called by handlers/services when they need to schedule a stock-up operation.
    /// If a StockUpOperation with the same DocumentNumber already exists, the create is
    /// skipped (idempotent no-op) instead of throwing a unique-constraint violation.
    /// </summary>
    /// <param name="persistImmediately">
    /// When true (default), the new operation is flushed to the database immediately via
    /// SaveChangesAsync, preserving today's behavior for existing callers (e.g.
    /// GiftPackageManufactureService). When false, the operation is only staged on the
    /// shared ApplicationDbContext's change tracker (via AddAsync) and the caller is
    /// responsible for a later SaveChangesAsync call that flushes it together with other
    /// pending changes, as one atomic commit. This parameter is deliberately placed after
    /// CancellationToken (not before) so every existing call site that passes a
    /// CancellationToken positionally as its last argument keeps compiling unchanged and
    /// keeps getting persistImmediately: true. Do not reorder this parameter.
    /// </param>
    Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default,
        bool persistImmediately = true);

    /// <summary>
    /// Processes all pending stock-up operations.
    /// Called by background task to submit operations to Shoptet.
    /// </summary>
    Task ProcessPendingOperationsAsync(CancellationToken ct = default);
}
