namespace Anela.Heblo.Domain.Features.Catalog;

public class ManufactureCost    
{
    public DateTime Date { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal HandlingCost { get; set; }
    public decimal Total => MaterialCost + HandlingCost;
}