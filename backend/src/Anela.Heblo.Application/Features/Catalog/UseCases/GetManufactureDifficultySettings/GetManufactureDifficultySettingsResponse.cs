using Anela.Heblo.Application.Features.Catalog.Contracts;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetManufactureDifficultySettings;

public class GetManufactureDifficultySettingsResponse
{
    public string ProductCode { get; set; } = null!;
    public List<ManufactureDifficultySettingDto> Settings { get; set; } = new();
    public ManufactureDifficultySettingDto? CurrentSetting { get; set; }
}