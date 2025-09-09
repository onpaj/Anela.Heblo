namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;

public class CalculatedIngredientDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double OriginalAmount { get; set; }
    public double CalculatedAmount { get; set; }
    public decimal Price { get; set; }
    public decimal StockTotal { get; set; }
}