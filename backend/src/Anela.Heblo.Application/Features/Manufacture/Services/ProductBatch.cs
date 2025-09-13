namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ProductBatch
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double BatchSize { get; set; }
    public double BatchCount { get; set; }
    public double TotalWeight { get; set; }
    public List<ProductVariant> Variants { get; set; }

    public List<ProductVariant> ValidVariants => Variants.Where(w => w.DailySales > 0 && w.Weight > 0).ToList();
}