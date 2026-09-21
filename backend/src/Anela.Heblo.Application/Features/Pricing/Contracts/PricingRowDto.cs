namespace Anela.Heblo.Application.Features.Pricing.Contracts;

public class PricingRowDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    // Baseline — never edited, used for the "before" column and drift detection
    public decimal BaselinePrice { get; set; }
    public decimal BaselineMaterialCost { get; set; }
    public decimal BaselineManufacturingCost { get; set; }
    public double BaselineQuantity { get; set; }

    // Effective = baseline with overrides applied
    public decimal Price { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal ManufacturingCost { get; set; }
    public double ForecastQuantity { get; set; }

    // Derived, never stored
    public decimal M0Amount { get; set; }
    public decimal M0Percentage { get; set; }
    public decimal M1Amount { get; set; }
    public decimal M1Percentage { get; set; }

    // Derived from the baseline inputs above using the same M0/M1 formula as the
    // effective amounts -- the "before" side of the M0/M1 Kč columns. Kept as plain
    // Kč amounts (no baseline percentage) since only the ceník export needs them.
    public decimal BaselineM0Amount { get; set; }
    public decimal BaselineM1Amount { get; set; }

    public bool IsEdited { get; set; }
    public bool IsExcluded { get; set; }

    /// <summary>
    /// True when this row came from a saved scenario whose snapshot no longer matches the
    /// catalog — the cost or price has moved since the scenario was saved.
    /// </summary>
    public bool BaselineDrifted { get; set; }
}
