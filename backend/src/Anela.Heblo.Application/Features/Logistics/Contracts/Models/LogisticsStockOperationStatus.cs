namespace Anela.Heblo.Application.Features.Logistics.Contracts.Models;

public class LogisticsStockOperationStatus
{
    public string DocumentNumber { get; init; } = string.Empty;
    public LogisticsStockOperationState State { get; init; }
}
