using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Purchase;

public class PurchaseOrderLine : IEntity<Guid>
{
    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public Guid MaterialId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public string? Notes { get; private set; }

    public decimal LineTotal => Quantity * UnitPrice;

    protected PurchaseOrderLine()
    {
    }

    public PurchaseOrderLine(Guid purchaseOrderId, Guid materialId, decimal quantity, decimal unitPrice, string? notes)
    {
        Id = Guid.NewGuid();
        PurchaseOrderId = purchaseOrderId;
        MaterialId = materialId;

        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity must be greater than zero", nameof(quantity));
        }
        Quantity = quantity;

        if (unitPrice < 0)
        {
            throw new ArgumentException("Unit price cannot be negative", nameof(unitPrice));
        }
        UnitPrice = unitPrice;

        Notes = notes;
    }

    internal void Update(decimal quantity, decimal unitPrice, string? notes)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity must be greater than zero", nameof(quantity));
        }
        Quantity = quantity;

        if (unitPrice < 0)
        {
            throw new ArgumentException("Unit price cannot be negative", nameof(unitPrice));
        }
        UnitPrice = unitPrice;

        Notes = notes;
    }
}