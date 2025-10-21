namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class LowStockProductData
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal EshopStock { get; set; }
    public decimal ReserveStock { get; set; }
    public decimal TransportStock { get; set; }
    public decimal AverageDailySales { get; set; }
    public decimal DaysOfStockRemaining { get; set; }
}