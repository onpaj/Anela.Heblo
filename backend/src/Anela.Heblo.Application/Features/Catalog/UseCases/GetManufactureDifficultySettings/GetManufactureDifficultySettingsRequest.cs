using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetManufactureDifficultySettings;

public class GetManufactureDifficultySettingsRequest : IRequest<GetManufactureDifficultySettingsResponse>
{
    public string ProductCode { get; set; } = null!;
    public DateTime? AsOfDate { get; set; }
}