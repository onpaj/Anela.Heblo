using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty;

public class CreateManufactureDifficultyResponse : BaseResponse
{
    public ManufactureDifficultySettingDto DifficultyHistory { get; set; } = null!;
}