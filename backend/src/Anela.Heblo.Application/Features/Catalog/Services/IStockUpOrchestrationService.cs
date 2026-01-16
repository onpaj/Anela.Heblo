using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface IStockUpOrchestrationService
{
    Task<StockUpOperationResult> ExecuteAsync(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default);
}
