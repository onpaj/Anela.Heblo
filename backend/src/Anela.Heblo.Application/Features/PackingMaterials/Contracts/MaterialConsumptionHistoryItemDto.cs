using Anela.Heblo.Domain.Features.PackingMaterials.Enums;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class MaterialConsumptionHistoryItemDto
{
    public HistoryRecordType RecordType { get; set; }
    public string RecordTypeText { get; set; } = string.Empty;
    public int PackingMaterialId { get; set; }
    public string MaterialName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; }

    public ConsumptionType? ConsumptionType { get; set; }
    public string? ConsumptionTypeText { get; set; }
    public string? InvoiceId { get; set; }
    public string? ProductCode { get; set; }
    public decimal? ProductQuantity { get; set; }
    public decimal? Amount { get; set; }

    public decimal? OldQuantity { get; set; }
    public decimal? NewQuantity { get; set; }
    public decimal? ChangeAmount { get; set; }
    public LogEntryType? LogType { get; set; }
    public string? LogTypeText { get; set; }
    public string? UserId { get; set; }
}
