namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public class ErpStockTakingRequest
{
    public string ProductCode { get; set; }
    public bool SoftStockTaking => StockTakingItems.All(s => s.SoftStockTaking);
    public List<ErpStockTakingLot> StockTakingItems { get; set; } = new();
    public bool RemoveMissingLots { get; set; } = false;
    public bool DryRun { get; set; } = true;
}