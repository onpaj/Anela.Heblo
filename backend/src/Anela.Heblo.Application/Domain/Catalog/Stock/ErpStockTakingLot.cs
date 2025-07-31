namespace Anela.Heblo.Application.Domain.Catalog.Stock;

public class ErpStockTakingLot
{
    public string? LotCode { get; set; }
    public DateTime? Expiration { get; set; }
    public decimal Amount { get; set; }
    public bool SoftStockTaking { get; set; } = true; // Default to true
}