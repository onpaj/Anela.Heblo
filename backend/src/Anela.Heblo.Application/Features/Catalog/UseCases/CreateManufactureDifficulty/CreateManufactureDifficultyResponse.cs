using Anela.Heblo.Application.Features.Catalog.Contracts;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty;

public class CreateManufactureDifficultyResponse
{
    public ManufactureDifficultySettingDto DifficultyHistory { get; set; } = null!;
}