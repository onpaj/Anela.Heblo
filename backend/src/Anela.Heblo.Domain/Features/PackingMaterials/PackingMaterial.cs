using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public class PackingMaterial : IEntity<int>
{
    public int Id { get; private set; }
    public string Name { get; private set; } = null!;
    public decimal ConsumptionRate { get; private set; }
    public ConsumptionType ConsumptionType { get; private set; }
    public decimal CurrentQuantity { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private readonly List<PackingMaterialLog> _logs = new();
    public IReadOnlyCollection<PackingMaterialLog> Logs => _logs.AsReadOnly();

    protected PackingMaterial()
    {
    }

    public PackingMaterial(
        string name,
        decimal consumptionRate,
        ConsumptionType consumptionType,
        decimal currentQuantity = 0)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ConsumptionRate = consumptionRate;
        ConsumptionType = consumptionType;
        CurrentQuantity = currentQuantity;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateMaterial(string name, decimal consumptionRate, ConsumptionType consumptionType)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ConsumptionRate = consumptionRate;
        ConsumptionType = consumptionType;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateQuantity(decimal newQuantity, DateOnly date, LogEntryType logType, string? userId = null)
    {
        var oldQuantity = CurrentQuantity;
        CurrentQuantity = newQuantity;
        UpdatedAt = DateTime.UtcNow;

        var log = new PackingMaterialLog(Id, date, oldQuantity, newQuantity, logType, userId);
        _logs.Add(log);
    }

    public decimal CalculateForecastedDays(List<PackingMaterialLog> recentLogs)
    {
        if (CurrentQuantity <= 0)
            return 0;

        var nonZeroConsumptions = recentLogs
            .Where(log => log.ChangeAmount < 0) // Only consumption entries (negative changes)
            .Select(log => Math.Abs(log.ChangeAmount))
            .Where(consumption => consumption > 0)
            .ToList();

        if (!nonZeroConsumptions.Any())
            return decimal.MaxValue; // No consumption history

        var averageDailyConsumption = nonZeroConsumptions.Average();
        return CurrentQuantity / averageDailyConsumption;
    }
}