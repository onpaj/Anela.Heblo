namespace Anela.Heblo.Application.Domain.Catalog.Stock;

public class ErpStock
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public decimal Stock { get; set; }
    public string MOQ { get; set; }
    public int? ProductTypeId { get; set; }
    public int ProductId { get; set; }
    public bool HasExpiration { get; set; }
    public bool HasLots { get; set; }
    public double Volume { get; set; }
    public double Weight { get; set; }
}