namespace Anela.Heblo.Domain.Features.Catalog;

public record CatalogProperties
{
    public int OptimalStockDaysSetup { get; set; } = 0;
    public decimal StockMinSetup { get; set; } = 0;
    public int BatchSize { get; set; } = 0;

    public int ExpirationMonths { get; set; } = 12;
    public int[] SeasonMonths { get; set; } = Array.Empty<int>();
    
    public double AllowedResiduePercentage { get; set; } = 100;
}