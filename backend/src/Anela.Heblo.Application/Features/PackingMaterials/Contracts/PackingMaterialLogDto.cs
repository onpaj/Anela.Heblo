using Anela.Heblo.Domain.Features.PackingMaterials.Enums;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class PackingMaterialLogDto
{
    public int Id { get; set; }
    public int PackingMaterialId { get; set; }
    public DateOnly Date { get; set; }
    public decimal OldQuantity { get; set; }
    public decimal NewQuantity { get; set; }
    public decimal ChangeAmount { get; set; }
    public LogEntryType LogType { get; set; }
    public string LogTypeText { get; set; } = null!;
    public string? UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}