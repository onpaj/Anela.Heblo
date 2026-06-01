using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public class PackingMaterialAllocation : IEntity<int>
{
    public int Id { get; private set; }
    public int PackingMaterialId { get; private set; }
    public string ProductCode { get; private set; } = null!;
    public decimal AmountPerUnit { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    protected PackingMaterialAllocation() { }

    public PackingMaterialAllocation(int packingMaterialId, string productCode, decimal amountPerUnit)
    {
        if (amountPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountPerUnit), "Must be greater than zero.");
        PackingMaterialId = packingMaterialId;
        ProductCode = productCode ?? throw new ArgumentNullException(nameof(productCode));
        AmountPerUnit = amountPerUnit;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateAllocation(string productCode, decimal amountPerUnit)
    {
        if (amountPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountPerUnit), "Must be greater than zero.");
        ProductCode = productCode ?? throw new ArgumentNullException(nameof(productCode));
        AmountPerUnit = amountPerUnit;
        UpdatedAt = DateTime.UtcNow;
    }
}
