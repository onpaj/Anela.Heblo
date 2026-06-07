using Anela.Heblo.Domain.Features.PackingMaterials.Enums;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public class MaterialConsumptionHistoryRecord
{
    public HistoryRecordType RecordType { get; set; }
    public int PackingMaterialId { get; set; }
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; }

    // Consumption-fact fields (null on quantity-change rows)
    public ConsumptionType? ConsumptionType { get; set; }
    public string? InvoiceId { get; set; }
    public string? ProductCode { get; set; }
    public decimal? ProductQuantity { get; set; }
    public decimal? Amount { get; set; }

    // Quantity-change fields (null on consumption-fact rows)
    public decimal? OldQuantity { get; set; }
    public decimal? NewQuantity { get; set; }
    public decimal? ChangeAmount { get; set; }
    public LogEntryType? LogType { get; set; }
    public string? UserId { get; set; }
}
