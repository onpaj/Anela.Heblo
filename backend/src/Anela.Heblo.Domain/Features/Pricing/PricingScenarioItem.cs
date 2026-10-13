namespace Anela.Heblo.Domain.Features.Pricing;

/// <summary>
/// One edited product inside a scenario. Overrides are the independent variables (price and
/// costs) — margins are always derived, never stored, so edit order cannot corrupt a reload.
/// The Baseline* columns snapshot what the catalog said at save time, so a scenario reopened
/// months later still shows what was actually decided against.
/// </summary>
public class PricingScenarioItem
{
    public Guid Id { get; set; }
    public Guid ScenarioId { get; set; }
    public PricingScenario? Scenario { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public decimal? Price { get; set; }
    public decimal? MaterialCost { get; set; }
    public decimal? ManufacturingCost { get; set; }
    public double? ForecastQuantity { get; set; }

    public decimal BaselinePrice { get; set; }
    public decimal BaselineMaterialCost { get; set; }
    public decimal BaselineManufacturingCost { get; set; }
    public double BaselineQuantity { get; set; }
}
