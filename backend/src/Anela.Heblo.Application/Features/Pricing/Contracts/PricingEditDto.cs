namespace Anela.Heblo.Application.Features.Pricing.Contracts;

// Members may be inserted mid-enum -- as MaterialCost/ManufacturingCost were -- only
// because the ordinal is never observable: Program.cs registers JsonStringEnumConverter
// globally so this crosses the wire by name, and a saved scenario stores the resolved
// Price/MaterialCost/ManufacturingCost/ForecastQuantity values rather than the field
// that produced them. Should either of those ever change, an insertion here would
// silently reinterpret every already-saved scenario, and members must then be appended.
public enum PricingEditField
{
    Price,
    MaterialCost,
    ManufacturingCost,
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
