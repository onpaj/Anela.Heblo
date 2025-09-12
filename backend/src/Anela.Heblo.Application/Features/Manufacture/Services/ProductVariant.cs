namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ProductVariant
{
    public double Volume { get; set; }
    public double Weight { get; set; }
    public double DailySales { get; set; }
    public double CurrentStock { get; set; }
    public double SuggestedAmount { get; set; }

    public double UpstockSuggested => DailySales > 0 ? SuggestedAmount / DailySales : 0;
    public double UpstockTotal => DailySales > 0 ? (SuggestedAmount + CurrentStock) / DailySales : 0;
    public double UpstockCurrent => DailySales > 0 ? CurrentStock / DailySales : 0;
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
}