using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetManufactureDifficultySettings;

public class GetManufactureDifficultySettingsResponse : BaseResponse
{
    public string ProductCode { get; set; } = null!;
    public List<ManufactureDifficultySettingDto> Settings { get; set; } = new();
    public ManufactureDifficultySettingDto? CurrentSetting { get; set; }
}