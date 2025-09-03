namespace Anela.Heblo.Domain.Features.Catalog;

public class ManufactureDifficultyConfiguration
{
    public List<ManufactureDifficultySetting> Settings { get; private set; } = new();

    public double? ManufactureDifficulty { get; private set; }
    public ManufactureDifficultySetting? GetDifficultyForDate(DateTime referenceDate)
    {
        var validSetting = Settings
            .Where(h => (h.ValidFrom == null || h.ValidFrom <= referenceDate) &&
                       (h.ValidTo == null || h.ValidTo >= referenceDate))
            .OrderByDescending(h => h.ValidFrom ?? DateTime.MinValue)
            .FirstOrDefault();

        return validSetting;
    }

    public void Assign(List<ManufactureDifficultySetting> settings, DateTime referenceDate)
    {
        Settings = settings;
        ManufactureDifficulty = GetDifficultyForDate(referenceDate)?.DifficultyValue;
    }
}