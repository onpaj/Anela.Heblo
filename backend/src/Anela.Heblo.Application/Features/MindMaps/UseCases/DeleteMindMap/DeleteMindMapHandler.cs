using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.DeleteMindMap;

public class DeleteMindMapHandler : IRequestHandler<DeleteMindMapRequest, DeleteMindMapResponse>
{
    private readonly IMindMapRepository _repository;

    public DeleteMindMapHandler(IMindMapRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeleteMindMapResponse> Handle(DeleteMindMapRequest request, CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (map is null)
        {
            return new DeleteMindMapResponse(ErrorCodes.ResourceNotFound);
        }

        await _repository.DeleteAsync(map, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return new DeleteMindMapResponse();
    }
}
