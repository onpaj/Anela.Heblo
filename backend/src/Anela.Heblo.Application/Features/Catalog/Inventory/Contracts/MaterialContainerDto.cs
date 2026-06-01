namespace Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;

public class MaterialContainerDto
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string? MaterialCode { get; set; }
    public string? LotCode { get; set; }
    public string Status { get; set; } = null!;
    public decimal? Amount { get; set; }
    public string? Unit { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public int? PurchaseOrderLineId { get; set; }
}
