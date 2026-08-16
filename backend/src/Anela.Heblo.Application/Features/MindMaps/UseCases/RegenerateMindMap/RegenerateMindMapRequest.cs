using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RegenerateMindMap;

public class RegenerateMindMapRequest : IRequest<RegenerateMindMapResponse>
{
    public Guid Id { get; set; }
}
