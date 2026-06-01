namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ResidueDistributionDto
{
    public decimal ActualSemiProductQuantity { get; set; }
    public decimal TheoreticalConsumption { get; set; }
    public decimal Difference { get; set; }
    public double DifferencePercentage { get; set; }
    public bool IsWithinAllowedThreshold { get; set; }
    public double AllowedResiduePercentage { get; set; }
    public List<ProductConsumptionDistributionDto> Products { get; set; } = new();
}

public class ProductConsumptionDistributionDto
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public decimal ActualPieces { get; set; }
    public decimal TheoreticalGramsPerUnit { get; set; }
    public decimal TheoreticalConsumption { get; set; }
    public decimal AdjustedConsumption { get; set; }
    public decimal AdjustedGramsPerUnit { get; set; }
    public double ProportionRatio { get; set; }
}
