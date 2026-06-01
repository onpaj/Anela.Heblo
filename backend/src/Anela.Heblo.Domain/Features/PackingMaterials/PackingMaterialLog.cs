using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public class PackingMaterialLog : IEntity<int>
{
    public int Id { get; private set; }
    public int PackingMaterialId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal OldQuantity { get; private set; }
    public decimal NewQuantity { get; private set; }
    public decimal ChangeAmount => NewQuantity - OldQuantity;
    public LogEntryType LogType { get; private set; }
    public string? UserId { get; private set; }
    public DateTime CreatedAt { get; private set; }


    protected PackingMaterialLog()
    {
    }

    public PackingMaterialLog(
        int packingMaterialId,
        DateOnly date,
        decimal oldQuantity,
        decimal newQuantity,
        LogEntryType logType,
        string? userId = null)
    {
        PackingMaterialId = packingMaterialId;
        Date = date;
        OldQuantity = oldQuantity;
        NewQuantity = newQuantity;
        LogType = logType;
        UserId = userId;
        CreatedAt = DateTime.UtcNow;
    }
}