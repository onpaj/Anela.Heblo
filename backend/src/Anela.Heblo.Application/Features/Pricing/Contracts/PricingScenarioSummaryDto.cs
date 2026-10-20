namespace Anela.Heblo.Application.Features.Pricing.Contracts;

public class PricingScenarioSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public int EditedProductCount { get; set; }
}
