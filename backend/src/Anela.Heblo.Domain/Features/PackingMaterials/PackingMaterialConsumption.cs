using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public class PackingMaterialConsumption : IEntity<int>
{
    public int Id { get; private set; }
    public int PackingMaterialId { get; private set; }
    public DateOnly Date { get; private set; }
    public ConsumptionType ConsumptionType { get; private set; }
    public string? InvoiceId { get; private set; }
    public string? ProductCode { get; private set; }
    public decimal? ProductQuantity { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime CreatedAt { get; private set; }

    protected PackingMaterialConsumption() { }

    public PackingMaterialConsumption(
        int packingMaterialId,
        DateOnly date,
        ConsumptionType consumptionType,
        decimal amount,
        string? invoiceId = null,
        string? productCode = null,
        decimal? productQuantity = null)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Cannot be negative.");
        PackingMaterialId = packingMaterialId;
        Date = date;
        ConsumptionType = consumptionType;
        Amount = amount;
        InvoiceId = invoiceId;
        ProductCode = productCode;
        ProductQuantity = productQuantity;
        CreatedAt = DateTime.UtcNow;
    }
}
