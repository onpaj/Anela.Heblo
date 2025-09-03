namespace Anela.Heblo.Application.Features.Catalog.Model;

public class GetManufactureDifficultySettingsResponse
{
    public string ProductCode { get; set; } = null!;
    public List<ManufactureDifficultySettingDto> Settings { get; set; } = new();
    public ManufactureDifficultySettingDto? CurrentSetting { get; set; }
}