namespace Anela.Heblo.Domain.Features.Manufacture;

public class SubmitManufactureClientRequest
{
    public string ManufactureOrderCode { get; set; } = null!;
    public string ManufactureInternalNumber { get; set; } = null!;
    public DateTime Date { get; set; }
    public string? CreatedBy { get; set; }
    public List<SubmitManufactureClientItem> Items { get; set; } = [];
    public ErpManufactureType ManufactureType { get; set; }

    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public bool ValidateIngredientStock { get; set; } = false;
    public ResidueDistribution? ResidueDistribution { get; set; }

    // Direct semiproduct output: bulk semiproduct sold as-is, needs a discard document
    public string? DirectSemiProductOutputCode { get; set; }
    public string? DirectSemiProductOutputName { get; set; }
    public decimal DirectSemiProductOutputAmount { get; set; }
}