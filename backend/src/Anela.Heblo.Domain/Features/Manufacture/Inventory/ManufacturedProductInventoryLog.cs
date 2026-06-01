using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Manufacture.Inventory;

public class ManufacturedProductInventoryLog : Entity<int>
{
    public int InventoryItemId { get; private set; }
    public InventoryChangeType ChangeType { get; private set; }
    public decimal AmountDelta { get; private set; }
    public decimal AmountAfter { get; private set; }
    public string? ReferenceType { get; private set; }
    public string? ReferenceId { get; private set; }
    public string? Note { get; private set; }
    public DateTime Timestamp { get; private set; }
    public string User { get; private set; } = null!;

    public ManufacturedProductInventoryLog(
        InventoryChangeType changeType,
        decimal amountDelta,
        decimal amountAfter,
        string user,
        DateTime timestamp,
        string? referenceType = null,
        string? referenceId = null,
        string? note = null)
    {
        ChangeType = changeType;
        AmountDelta = amountDelta;
        AmountAfter = amountAfter;
        User = user;
        Timestamp = timestamp;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        Note = note;
    }

    private ManufacturedProductInventoryLog() { }
}
