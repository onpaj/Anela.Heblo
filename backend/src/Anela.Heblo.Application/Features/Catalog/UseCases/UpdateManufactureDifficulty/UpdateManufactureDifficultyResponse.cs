using Anela.Heblo.Application.Features.Catalog.Contracts;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.UpdateManufactureDifficulty;

public class UpdateManufactureDifficultyResponse
{
    public ManufactureDifficultySettingDto DifficultyHistory { get; set; } = null!;
}