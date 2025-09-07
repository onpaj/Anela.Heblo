using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Purchase;

public class PurchaseOrder : IEntity<int>
{
    public int Id { get; private set; }
    public string OrderNumber { get; private set; } = null!;
    public long SupplierId { get; private set; }
    public string SupplierName { get; private set; } = null!;
    public DateTime OrderDate { get; private set; }
    public DateTime? ExpectedDeliveryDate { get; private set; }
    public ContactVia? ContactVia { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public bool InvoiceAcquired { get; private set; }
    public string? Notes { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private readonly List<PurchaseOrderLine> _lines = new();
    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    private readonly List<PurchaseOrderHistory> _history = new();
    public IReadOnlyCollection<PurchaseOrderHistory> History => _history.AsReadOnly();

    public decimal TotalAmount => _lines.Sum(l => l.LineTotal);

    public bool IsEditable => Status != PurchaseOrderStatus.Completed;

    protected PurchaseOrder()
    {
    }

    public PurchaseOrder(
        string orderNumber,
        long supplierId,
        string supplierName,
        DateTime orderDate,
        DateTime? expectedDeliveryDate,
        ContactVia? contactVia,
        string? notes,
        string createdBy)
    {
        OrderNumber = orderNumber ?? throw new ArgumentNullException(nameof(orderNumber));
        SupplierId = supplierId;
        SupplierName = supplierName ?? throw new ArgumentNullException(nameof(supplierName));
        OrderDate = orderDate.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(orderDate, DateTimeKind.Utc) : orderDate.ToUniversalTime();
        ExpectedDeliveryDate = expectedDeliveryDate?.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(expectedDeliveryDate.Value, DateTimeKind.Utc) : expectedDeliveryDate?.ToUniversalTime();
        ContactVia = contactVia;
        Status = PurchaseOrderStatus.Draft;
        InvoiceAcquired = false;
        Notes = notes;
        CreatedBy = createdBy ?? throw new ArgumentNullException(nameof(createdBy));
        CreatedAt = DateTime.UtcNow;

        AddHistoryEntry($"Order created", null, Status.ToString(), createdBy);
    }

    public void AddLine(string materialId, string materialName, decimal quantity, decimal unitPrice, string? notes)
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Cannot add lines to completed orders");
        }

        var line = new PurchaseOrderLine(Id, materialId, materialName, quantity, unitPrice, notes);
        _lines.Add(line);

        // Debug logging
        Console.WriteLine($"Added line {line.Id} to purchase order {Id}. Total lines: {_lines.Count}");

        UpdatedBy = CreatedBy;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RemoveLine(int lineId)
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Cannot remove lines from completed orders");
        }

        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line != null)
        {
            _lines.Remove(line);
            UpdatedBy = CreatedBy;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void UpdateLine(int lineId, string materialName, decimal quantity, decimal unitPrice, string? notes)
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Cannot update lines in completed orders");
        }

        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line != null)
        {
            line.Update(materialName, quantity, unitPrice, notes);
            UpdatedBy = CreatedBy;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void ClearAllLines()
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Cannot clear lines from completed orders");
        }

        _lines.Clear();
        UpdatedBy = CreatedBy;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(long supplierId, string supplierName, DateTime? expectedDeliveryDate, ContactVia? contactVia, string? notes, string updatedBy)
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Cannot update completed orders");
        }

        SupplierId = supplierId;
        SupplierName = supplierName ?? throw new ArgumentNullException(nameof(supplierName));
        ExpectedDeliveryDate = expectedDeliveryDate?.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(expectedDeliveryDate.Value, DateTimeKind.Utc) : expectedDeliveryDate?.ToUniversalTime();
        ContactVia = contactVia;
        Notes = notes;
        UpdatedBy = updatedBy;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateOrderNumber(string orderNumber, string updatedBy)
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Cannot update order number for completed orders");
        }

        var oldOrderNumber = OrderNumber;
        OrderNumber = orderNumber ?? throw new ArgumentNullException(nameof(orderNumber));
        UpdatedBy = updatedBy;
        UpdatedAt = DateTime.UtcNow;

        AddHistoryEntry($"Order number changed", oldOrderNumber, orderNumber, updatedBy);
    }

    public void ChangeStatus(PurchaseOrderStatus newStatus, string changedBy)
    {
        if (!IsValidStatusTransition(Status, newStatus))
        {
            throw new InvalidOperationException($"Invalid status transition from {Status} to {newStatus}");
        }

        var oldStatus = Status.ToString();
        Status = newStatus;
        UpdatedBy = changedBy;
        UpdatedAt = DateTime.UtcNow;

        AddHistoryEntry($"Status changed from {oldStatus} to {newStatus}", oldStatus, newStatus.ToString(), changedBy);
    }

    public void SetInvoiceAcquired(bool invoiceAcquired, string changedBy)
    {
        var oldValue = InvoiceAcquired.ToString();
        InvoiceAcquired = invoiceAcquired;
        UpdatedBy = changedBy;
        UpdatedAt = DateTime.UtcNow;

        AddHistoryEntry($"Invoice acquisition changed", oldValue, invoiceAcquired.ToString(), changedBy);
    }

    private bool IsValidStatusTransition(PurchaseOrderStatus from, PurchaseOrderStatus to)
    {
        return (from, to) switch
        {
            (PurchaseOrderStatus.Draft, PurchaseOrderStatus.InTransit) => true,
            (PurchaseOrderStatus.InTransit, PurchaseOrderStatus.Completed) => true,
            _ => false
        };
    }

    private void AddHistoryEntry(string action, string? oldValue, string? newValue, string changedBy)
    {
        var historyEntry = new PurchaseOrderHistory(Id, action, oldValue, newValue, changedBy);
        _history.Add(historyEntry);
    }
}