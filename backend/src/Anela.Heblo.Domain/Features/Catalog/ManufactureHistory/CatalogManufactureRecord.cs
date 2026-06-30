namespace Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;

public class CatalogManufactureRecord
{
    public DateTime Date { get; set; }
    public double Amount { get; set; }
    public decimal PricePerPiece { get; set; }
    public decimal PriceTotal { get; set; }
    public string ProductCode { get; set; }
    public string DocumentNumber { get; set; }
}
