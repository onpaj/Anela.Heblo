namespace Anela.Heblo.Domain.Features.Catalog.Stock;


public class EshopStock
{
    public string Code { get; set; }
    public string PairCode { get; set; }
    public string Name { get; set; }
    public decimal Stock { get; set; }
    public string NameSuffix { get; set; }
    public string Location { get; set; }
    public string? DefaultImage { get; set; }
    public string? Image { get; set; }
    public double? Weight { get; set; }
    public double? Height { get; set; }
    public double? Depth { get; set; }
    public double? Width { get; set; }
    public bool AtypicalShipping { get; set; } = false;
}