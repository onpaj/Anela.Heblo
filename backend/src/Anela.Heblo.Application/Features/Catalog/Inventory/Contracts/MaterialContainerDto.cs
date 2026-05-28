namespace Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;

public class MaterialContainerDto
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public int LotId { get; set; }
    public decimal Amount { get; set; }
    public string Unit { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}
