using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public class MaterialContainer : IEntity<int>
{
    protected MaterialContainer() { } // EF Core

    public MaterialContainer(
        string code, string materialCode, string lotCode,
        decimal? amount, string? unit, string createdBy,
        int? purchaseOrderLineId = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(materialCode)) throw new ArgumentException("MaterialCode is required.", nameof(materialCode));
        if (string.IsNullOrWhiteSpace(lotCode)) throw new ArgumentException("LotCode is required.", nameof(lotCode));
        if (amount is <= 0) throw new ArgumentException("Amount must be positive when provided.", nameof(amount));
        if (unit is not null && string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Unit must be non-empty when provided.", nameof(unit));
        if (string.IsNullOrWhiteSpace(createdBy)) throw new ArgumentException("CreatedBy is required.", nameof(createdBy));

        Code = code;
        MaterialCode = materialCode;
        LotCode = lotCode;
        Amount = amount;
        Unit = unit;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
        Status = MaterialContainerStatus.Assigned;
        PurchaseOrderLineId = purchaseOrderLineId;
    }

    public int Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string MaterialCode { get; private set; } = null!;
    public string LotCode { get; private set; } = null!;
    public decimal? Amount { get; private set; }
    public string? Unit { get; private set; }
    public MaterialContainerStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public int? PurchaseOrderLineId { get; private set; }

    public void Discard(string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(updatedBy)) throw new ArgumentException("UpdatedBy is required.", nameof(updatedBy));
        if (Status == MaterialContainerStatus.Discarded) return;
        Status = MaterialContainerStatus.Discarded;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}
