namespace Anela.Heblo.Domain.Features.Catalog.Lots;

public class CatalogLot
{
    public string ProductCode { get; set; }
    public decimal Amount { get; set; }
    public DateOnly? Expiration { get; set; }
    public string? Lot { get; set; }
}