namespace Anela.Heblo.Domain.Features.Pricing;

public class PricingScenario
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    /// <summary>The filter the scenario was built under, so reopening restores the same set.</summary>
    public string FilterJson { get; set; } = "{}";

    public ICollection<PricingScenarioItem> Items { get; set; } = new List<PricingScenarioItem>();
}
