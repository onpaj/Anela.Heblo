namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ProductPropertiesSnapshot
{
    public int OptimalStockDaysSetup { get; init; }
    public decimal StockMinSetup { get; init; }
    public int BatchSize { get; init; }
    public int ExpirationMonths { get; init; } = 12;
    public double AllowedResiduePercentage { get; init; }
}
