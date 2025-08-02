using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Purchase;

public class PurchaseOrderHistory : IEntity<Guid>
{
    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public string Action { get; private set; } = null!;
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public string ChangedBy { get; private set; } = null!;
    public DateTime ChangedAt { get; private set; }

    protected PurchaseOrderHistory()
    {
    }

    public PurchaseOrderHistory(Guid purchaseOrderId, string action, string? oldValue, string? newValue, string changedBy)
    {
        Id = Guid.NewGuid();
        PurchaseOrderId = purchaseOrderId;
        Action = action ?? throw new ArgumentNullException(nameof(action));
        OldValue = oldValue;
        NewValue = newValue;
        ChangedBy = changedBy ?? throw new ArgumentNullException(nameof(changedBy));
        ChangedAt = DateTime.UtcNow;
    }
}