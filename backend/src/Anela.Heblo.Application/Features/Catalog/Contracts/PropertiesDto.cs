using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class PropertiesDto
{
    public int OptimalStockDaysSetup { get; set; }
    public decimal StockMinSetup { get; set; }
    public int BatchSize { get; set; }
    public int[] SeasonMonths { get; set; } = Array.Empty<int>();
    public Cooling Cooling { get; set; }
}