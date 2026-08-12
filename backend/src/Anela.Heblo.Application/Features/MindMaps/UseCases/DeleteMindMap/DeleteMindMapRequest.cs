using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.DeleteMindMap;

public class DeleteMindMapRequest : IRequest<DeleteMindMapResponse>
{
    public Guid Id { get; set; }
}
