namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public class PurchaseOrderHistoryDto
{
    public int Id { get; set; }
    public string Action { get; set; } = null!;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime ChangedAt { get; set; }
    public string ChangedBy { get; set; } = null!;
}