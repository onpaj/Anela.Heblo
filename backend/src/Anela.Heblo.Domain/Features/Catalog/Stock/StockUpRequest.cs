namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public class StockUpRequest
{
    public StockUpRequest()
    {
        
    }
    public StockUpRequest(string productCode, double amount, string? stockUpId = null)
    {
        Products.Add(new StockUpProductRequest()
        {
            ProductCode = productCode,
            Amount = amount,
        });
        StockUpId = stockUpId;
    }
    
    public string StockUpId { get; set; }
    public List<StockUpProductRequest> Products { get; set; } = new List<StockUpProductRequest>();
}

public class StockUpProductRequest
{
    public string ProductCode { get; set; }
    public double Amount { get; set; }
}