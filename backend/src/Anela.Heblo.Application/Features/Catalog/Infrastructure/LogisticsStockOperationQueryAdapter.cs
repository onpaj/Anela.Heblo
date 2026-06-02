using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class LogisticsStockOperationQueryAdapter : ILogisticsStockOperationQueryService
{
    private readonly IStockUpOperationRepository _repository;

    public LogisticsStockOperationQueryAdapter(IStockUpOperationRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<LogisticsStockOperationStatus>> GetOperationsBySourceAsync(
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default)
    {
        var mappedSourceType = MapSourceType(sourceType);
        var operations = await _repository.GetBySourceAsync(mappedSourceType, sourceId, cancellationToken);

        var result = new List<LogisticsStockOperationStatus>(operations.Count);
        foreach (var operation in operations)
        {
            result.Add(new LogisticsStockOperationStatus
            {
                DocumentNumber = operation.DocumentNumber,
                State = MapState(operation.State),
            });
        }

        return result;
    }

    private static StockUpSourceType MapSourceType(LogisticsStockOperationSource sourceType) => sourceType switch
    {
        LogisticsStockOperationSource.TransportBox => StockUpSourceType.TransportBox,
        LogisticsStockOperationSource.GiftPackageManufacture => StockUpSourceType.GiftPackageManufacture,
        _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, null),
    };

    private static LogisticsStockOperationState MapState(StockUpOperationState state) => state switch
    {
        StockUpOperationState.Pending => LogisticsStockOperationState.Pending,
        StockUpOperationState.Submitted => LogisticsStockOperationState.Submitted,
        StockUpOperationState.Completed => LogisticsStockOperationState.Completed,
        StockUpOperationState.Failed => LogisticsStockOperationState.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
