using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Purchase;

public class PurchaseOrderLine : IEntity<int>
{
    public int Id { get; private set; }
    public int PurchaseOrderId { get; private set; }
    public string MaterialId { get; private set; }
    public string MaterialName { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public string? Notes { get; private set; }

    public decimal LineTotal => Quantity * UnitPrice;

    protected PurchaseOrderLine()
    {
    }

    public PurchaseOrderLine(int purchaseOrderId, string materialId, string materialName, decimal quantity, decimal unitPrice, string? notes)
    {
        PurchaseOrderId = purchaseOrderId;
        
        if (string.IsNullOrWhiteSpace(materialId))
        {
            throw new ArgumentException("Material ID cannot be null or empty", nameof(materialId));
        }
        MaterialId = materialId;

        if (string.IsNullOrWhiteSpace(materialName))
        {
            throw new ArgumentException("Material name cannot be null or empty", nameof(materialName));
        }
        MaterialName = materialName;

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

    internal void Update(string materialName, decimal quantity, decimal unitPrice, string? notes)
    {
        if (string.IsNullOrWhiteSpace(materialName))
        {
            throw new ArgumentException("Material name cannot be null or empty", nameof(materialName));
        }
        MaterialName = materialName;

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