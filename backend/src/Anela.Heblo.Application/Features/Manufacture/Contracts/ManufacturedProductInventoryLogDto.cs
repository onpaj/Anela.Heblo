using Anela.Heblo.Domain.Features.Manufacture.Inventory;

namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ManufacturedProductInventoryLogDto
{
    public int Id { get; set; }
    public int InventoryItemId { get; set; }
    public InventoryChangeType ChangeType { get; set; }
    public decimal AmountDelta { get; set; }
    public decimal AmountAfter { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceId { get; set; }
    public string? Note { get; set; }
    public DateTime Timestamp { get; set; }
    public string User { get; set; } = null!;
}
