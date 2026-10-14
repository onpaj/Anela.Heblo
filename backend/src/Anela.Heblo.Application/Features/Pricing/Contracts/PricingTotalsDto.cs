namespace Anela.Heblo.Application.Features.Pricing.Contracts;

public class PricingTotalsDto
{
    public decimal RevenueBefore { get; set; }
    public decimal RevenueAfter { get; set; }
    public decimal RevenueDelta { get; set; }
    public decimal RevenueDeltaPercentage { get; set; }

    public decimal M0Before { get; set; }
    public decimal M0After { get; set; }
    public decimal M0Delta { get; set; }
    public decimal M0DeltaPercentage { get; set; }

    public decimal M1Before { get; set; }
    public decimal M1After { get; set; }
    public decimal M1Delta { get; set; }
    public decimal M1DeltaPercentage { get; set; }

    public int EditedProductCount { get; set; }
    public int ExcludedProductCount { get; set; }
}
