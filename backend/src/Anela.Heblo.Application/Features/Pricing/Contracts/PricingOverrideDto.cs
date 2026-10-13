namespace Anela.Heblo.Application.Features.Pricing.Contracts;

public class PricingOverrideDto
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public decimal? MaterialCost { get; set; }
    public decimal? ManufacturingCost { get; set; }
    public double? ForecastQuantity { get; set; }
}
