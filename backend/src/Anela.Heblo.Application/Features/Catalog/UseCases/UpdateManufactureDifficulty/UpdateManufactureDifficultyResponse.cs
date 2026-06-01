using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.UpdateManufactureDifficulty;

public class UpdateManufactureDifficultyResponse : BaseResponse
{
    public ManufactureDifficultySettingDto DifficultyHistory { get; set; } = null!;
}