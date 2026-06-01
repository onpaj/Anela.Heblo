using MediatR;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;

public class ResetGridLayoutRequest : IRequest<ResetGridLayoutResponse>
{
    public string GridKey { get; set; } = string.Empty;
}
