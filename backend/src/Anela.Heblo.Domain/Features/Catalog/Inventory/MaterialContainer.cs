using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public class MaterialContainer : IEntity<int>
{
    protected MaterialContainer() { } // EF Core

    public MaterialContainer(string code, int lotId, decimal amount, string unit, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (lotId <= 0) throw new ArgumentException("LotId must be positive.", nameof(lotId));
        if (amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(amount));
        if (string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Unit is required.", nameof(unit));
        if (string.IsNullOrWhiteSpace(createdBy)) throw new ArgumentException("CreatedBy is required.", nameof(createdBy));

        Code = code;
        LotId = lotId;
        Amount = amount;
        Unit = unit;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
    }

    public int Id { get; private set; }
    public string Code { get; private set; } = null!;
    public int LotId { get; private set; }
    public decimal Amount { get; private set; }
    public string Unit { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
}
