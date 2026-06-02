using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class LogisticsStockOperationAdapter : ILogisticsStockOperationService
{
    private readonly IStockUpProcessingService _stockUpProcessingService;

    public LogisticsStockOperationAdapter(IStockUpProcessingService stockUpProcessingService)
    {
        _stockUpProcessingService = stockUpProcessingService;
    }

    public Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default)
    {
        var mappedSourceType = MapSourceType(sourceType);
        return _stockUpProcessingService.CreateOperationAsync(
            documentNumber,
            productCode,
            amount,
            mappedSourceType,
            sourceId,
            cancellationToken);
    }

    private static StockUpSourceType MapSourceType(LogisticsStockOperationSource sourceType) => sourceType switch
    {
        LogisticsStockOperationSource.TransportBox => StockUpSourceType.TransportBox,
        LogisticsStockOperationSource.GiftPackageManufacture => StockUpSourceType.GiftPackageManufacture,
        _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, null),
    };
}
