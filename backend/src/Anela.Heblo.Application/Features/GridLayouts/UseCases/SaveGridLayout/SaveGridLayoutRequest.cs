using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;

public class SaveGridLayoutRequest : IRequest<SaveGridLayoutResponse>
{
    public string GridKey { get; set; } = string.Empty;
    public List<GridColumnStateDto> Columns { get; set; } = new();
}
