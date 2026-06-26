namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public sealed class MaterialStockSnapshot
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public required string ProductNameNormalized { get; init; }
    public required MaterialProductType ProductType { get; init; }
    public string? SupplierName { get; init; }
    public required string MinimalOrderQuantity { get; init; }
    public required bool IsMinStockConfigured { get; init; }
    public required bool IsOptimalStockConfigured { get; init; }
    public required MaterialStockLevels Stock { get; init; }
    public required decimal StockMinSetup { get; init; }
    public required int OptimalStockDaysSetup { get; init; }
    public required double ConsumptionInPeriod { get; init; }
    public MaterialPurchaseSnapshot? LastPurchase { get; init; }
}
