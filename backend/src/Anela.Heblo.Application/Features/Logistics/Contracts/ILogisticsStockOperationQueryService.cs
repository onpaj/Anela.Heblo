using Anela.Heblo.Application.Features.Logistics.Contracts.Models;

namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsStockOperationQueryService
{
    Task<IReadOnlyList<LogisticsStockOperationStatus>> GetOperationsBySourceAsync(
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default);
}
