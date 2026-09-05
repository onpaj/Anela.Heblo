namespace Anela.Heblo.Application.Features.ProductPricing.Services;

public class PriceSyncRunResult
{
    public int Pushed { get; set; }
    public int Conflicts { get; set; }
    public int Failed { get; set; }
    public int Seeded { get; set; }
    public int Unchanged { get; set; }
}
