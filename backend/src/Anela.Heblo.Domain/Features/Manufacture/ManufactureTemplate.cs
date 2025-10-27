namespace Anela.Heblo.Domain.Features.Manufacture;

public class ManufactureTemplate
{
    public int TemplateId { get; set; }
    public string ProductCode { get; set; }

    public string ProductName { get; set; }
    public double Amount { get; set; }
    public double OriginalAmount { get; set; }

    public List<Ingredient> Ingredients { get; set; } = new List<Ingredient>();
    public double BatchSize { get; set; }
    public ManufactureType ManufactureType { get; set; }
}