namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;

public class SemiproductRecipeData
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public double BatchSize { get; set; } = 0.0;
    public DateTime PrintedAt { get; set; } = DateTime.Now;
    public int? ExpirationMonths { get; set; }
    public List<SemiproductRecipeIngredientLine> Ingredients { get; set; } = new();
}

public class SemiproductRecipeIngredientLine
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public double AmountFullBatch { get; set; } = 0.0;
    public double AmountHalfBatch { get; set; } = 0.0;
    public double Percentage { get; set; } = 0.0;
}
