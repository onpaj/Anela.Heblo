namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public class EshopStockTakingRequest
{
    public string ProductCode { get; set; }
    public decimal TargetAmount { get; set; }
    public bool SoftStockTaking { get; set; }
}