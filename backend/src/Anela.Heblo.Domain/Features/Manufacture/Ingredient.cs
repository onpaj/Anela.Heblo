using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Domain.Features.Manufacture;

public class Ingredient
{
    public int TemplateId { get; set; }
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double Amount { get; set; }
    public double OriginalAmount { get; set; }
    public decimal Price { get; set; }
    public ProductType ProductType { get; set; }
    public bool HasLots { get; set; }
    public bool HasExpiration { get; set; }
    /// <summary>Display order from Abra Flexi BoM (poradi). 0 means unordered.</summary>
    public int Order { get; set; }
    /// <summary>Manufacture phase label (single uppercase letter A–Z) from Flexi nazevC. Null means unassigned.</summary>
    public string? PhaseLabel { get; set; }
}