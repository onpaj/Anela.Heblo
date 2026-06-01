namespace Anela.Heblo.Application.Features.Logistics.Contracts.Models;

public sealed class LogisticsGiftPackageItem
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public string? Image { get; init; }
    public decimal AvailableStock { get; init; }
    public double TotalSoldInPeriod { get; init; }
    public int StockMinSetup { get; init; }
    public int OptimalStockDaysSetup { get; init; }
}
