namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class StockDto
{
    public decimal Eshop { get; set; }
    public decimal Erp { get; set; }
    public decimal Transport { get; set; }
    public decimal Reserve { get; set; }
    public decimal Ordered { get; set; }
    public decimal Available { get; set; }
}