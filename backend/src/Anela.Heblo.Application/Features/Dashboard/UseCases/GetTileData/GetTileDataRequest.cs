using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;

public class GetTileDataRequest : IRequest<GetTileDataResponse>
{
    public string UserId { get; set; } = string.Empty;
}