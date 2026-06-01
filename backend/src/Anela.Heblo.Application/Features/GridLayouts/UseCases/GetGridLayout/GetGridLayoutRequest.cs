using MediatR;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;

public class GetGridLayoutRequest : IRequest<GetGridLayoutResponse>
{
    public string GridKey { get; set; } = string.Empty;
}
