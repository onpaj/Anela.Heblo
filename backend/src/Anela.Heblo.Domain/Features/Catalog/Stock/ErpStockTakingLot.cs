namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public class ErpStockTakingLot
{
    public string? LotCode { get; set; }
    public DateOnly? Expiration { get; set; }
    public decimal Amount { get; set; }
    public bool SoftStockTaking { get; set; } = true; // Default to true
}