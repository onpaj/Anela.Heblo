using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class GetManufactureDifficultySettingsRequest : IRequest<GetManufactureDifficultySettingsResponse>
{
    public string ProductCode { get; set; } = null!;
    public DateTime? AsOfDate { get; set; }
}