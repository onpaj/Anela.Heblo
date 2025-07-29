namespace Anela.Heblo.Application.Domain.Catalog.Lots;

public class CatalogLot
{
    public string ProductCode { get; set; }
    public decimal Amount { get; set; }
    public DateTime? Expiration { get; set; }
    public string? Lot { get; set; }
}