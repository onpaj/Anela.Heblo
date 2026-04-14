namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public class StockUpRequest
{
    public StockUpRequest(string productCode, double amount, string? stockUpId = null)
    {
        ProductCode = productCode;
        Amount = amount;
        StockUpId = stockUpId;
    }

    public string ProductCode { get; set; }
    public double Amount { get; set; }
    public string? StockUpId { get; set; }
}
