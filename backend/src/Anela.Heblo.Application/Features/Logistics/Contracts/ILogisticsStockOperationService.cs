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
}
