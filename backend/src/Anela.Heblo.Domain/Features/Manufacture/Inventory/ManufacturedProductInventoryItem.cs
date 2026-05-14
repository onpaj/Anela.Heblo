using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Manufacture.Inventory;

public class ManufacturedProductInventoryItem : Entity<int>
{
    private readonly List<ManufacturedProductInventoryLog> _log = new();

    public string ProductCode { get; private set; } = null!;
    public string ProductName { get; private set; } = null!;
    public string? LotNumber { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public decimal Amount { get; private set; }
    public int? ManufactureOrderId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime? LastModifiedAt { get; private set; }
    public string? LastModifiedBy { get; private set; }

    public IReadOnlyList<ManufacturedProductInventoryLog> Log => _log;

    public ManufacturedProductInventoryItem(
        string productCode,
        string productName,
        decimal amount,
        string createdBy,
        DateTime createdAt,
        string? lotNumber = null,
        DateOnly? expirationDate = null,
        int? manufactureOrderId = null)
    {
        ProductCode = productCode;
        ProductName = productName;
        Amount = amount;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        LotNumber = lotNumber;
        ExpirationDate = expirationDate;
        ManufactureOrderId = manufactureOrderId;

        _log.Add(new ManufacturedProductInventoryLog(
            InventoryChangeType.InitialWriteDown,
            amountDelta: amount,
            amountAfter: amount,
            user: createdBy,
            timestamp: createdAt,
            referenceType: manufactureOrderId.HasValue ? "ManufactureOrder" : null,
            referenceId: manufactureOrderId?.ToString()));
    }

    private ManufacturedProductInventoryItem() { }

    public void Consume(decimal amount, string user, DateTime timestamp, int? transportBoxId = null, string? transportBoxCode = null, bool allowNegativeStock = false)
    {
        if (!allowNegativeStock && amount > Amount)
            throw new InvalidOperationException(
                $"Insufficient manufactured inventory for {ProductCode}. Available: {Amount}, requested: {amount}.");

        Amount -= amount;
        LastModifiedAt = timestamp;
        LastModifiedBy = user;
        _log.Add(new ManufacturedProductInventoryLog(
            InventoryChangeType.ConsumedByTransportBox, -amount, Amount, user, timestamp,
            transportBoxId.HasValue ? "TransportBox" : null, transportBoxId?.ToString(),
            note: transportBoxCode));
    }

    public void Restore(decimal amount, string user, DateTime timestamp, int? transportBoxId = null, string? transportBoxCode = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Restore amount must be positive.", nameof(amount));

        Amount += amount;
        LastModifiedAt = timestamp;
        LastModifiedBy = user;
        _log.Add(new ManufacturedProductInventoryLog(
            InventoryChangeType.RestoredFromTransportBox, amount, Amount, user, timestamp,
            transportBoxId.HasValue ? "TransportBox" : null, transportBoxId?.ToString(),
            note: transportBoxCode));
    }

    public void ManualAdjust(decimal newAmount, string user, DateTime timestamp, string? note = null)
    {
        var delta = newAmount - Amount;
        Amount = newAmount;
        LastModifiedAt = timestamp;
        LastModifiedBy = user;
        _log.Add(new ManufacturedProductInventoryLog(
            InventoryChangeType.ManualAdjustment, delta, Amount, user, timestamp, note: note));
    }

    public void ManualRemove(string user, DateTime timestamp, string? note = null)
    {
        var delta = -Amount;
        Amount = 0;
        LastModifiedAt = timestamp;
        LastModifiedBy = user;
        _log.Add(new ManufacturedProductInventoryLog(
            InventoryChangeType.ManualRemoval, delta, 0m, user, timestamp, note: note));
    }
}
