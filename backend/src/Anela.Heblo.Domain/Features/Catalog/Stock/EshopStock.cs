namespace Anela.Heblo.Domain.Features.Catalog.Stock;


public class EshopStock
{
    public string Code { get; set; }
    public string PairCode { get; set; }
    public string Name { get; set; }
    public decimal Stock { get; set; }
    public string NameSuffix { get; set; }
    public string Location { get; set; }
}