namespace Anela.Heblo.Domain.Features.Manufacture;

public class Ingredient
{
    public int TemplateId { get; set; }
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double Amount { get; set; }
    public double OriginalAmount { get; set; }
    public decimal Price { get; set; }
}