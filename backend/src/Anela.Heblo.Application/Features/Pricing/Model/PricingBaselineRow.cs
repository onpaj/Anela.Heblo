namespace Anela.Heblo.Application.Features.Pricing.Model;

/// <summary>
/// One product's untouched starting point, built from the catalog. Internal to the
/// Pricing slice — never crosses the API boundary, so a record is fine here.
/// </summary>
public record PricingBaselineRow(
    string ProductCode,
    string ProductName,
    decimal Price,
    decimal MaterialCost,
    decimal ManufacturingCost,
    double Quantity,
    bool HasData);
