namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public class StockUpRequest
{
    public string StockUpId { get; set; }
    public string Product { get; set; }
    public double Amount { get; set; }
}