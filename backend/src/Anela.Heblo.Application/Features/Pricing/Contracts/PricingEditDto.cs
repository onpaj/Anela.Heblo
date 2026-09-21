namespace Anela.Heblo.Application.Features.Pricing.Contracts;

public enum PricingEditField
{
    Price,
    M0Amount,
    M0Percentage,
    M1Amount,
    M1Percentage,
    ForecastQuantity
}

public class PricingEditDto
{
    public string ProductCode { get; set; } = string.Empty;
    public PricingEditField Field { get; set; }
    public decimal Value { get; set; }
}
