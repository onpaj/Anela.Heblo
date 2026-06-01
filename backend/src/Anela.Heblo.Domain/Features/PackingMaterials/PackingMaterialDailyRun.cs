using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public class PackingMaterialDailyRun : IEntity<int>
{
    public int Id { get; private set; }
    public DateOnly Date { get; private set; }
    public DateTime ProcessedAt { get; private set; }
    public int MaterialsProcessed { get; private set; }

    protected PackingMaterialDailyRun() { }

    public PackingMaterialDailyRun(DateOnly date, int materialsProcessed)
    {
        Date = date;
        ProcessedAt = DateTime.UtcNow;
        MaterialsProcessed = materialsProcessed;
    }
}
