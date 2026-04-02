namespace Anela.Heblo.Domain.Features.Manufacture;

public class ResidueDistribution
{
    public decimal ActualSemiProductQuantity { get; set; }
    public decimal TheoreticalConsumption { get; set; }
    public decimal Difference { get; set; }           // positive=residue, negative=deficit
    public double DifferencePercentage { get; set; }  // |diff| / theoretical * 100
    public bool IsWithinAllowedThreshold { get; set; }
    public double AllowedResiduePercentage { get; set; }
    public List<ProductConsumptionDistribution> Products { get; set; } = new();
}

public class ProductConsumptionDistribution
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public decimal ActualPieces { get; set; }
    public decimal TheoreticalGramsPerUnit { get; set; }
    public decimal TheoreticalConsumption { get; set; }
    public decimal AdjustedConsumption { get; set; }  // proportional share of actual semiproduct
    public decimal AdjustedGramsPerUnit { get; set; } // AdjustedConsumption / ActualPieces — for BoM update
    public double ProportionRatio { get; set; }
}
