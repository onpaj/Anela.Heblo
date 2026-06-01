using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;

public class DeletePackingMaterialHandler : IRequestHandler<DeletePackingMaterialRequest, DeletePackingMaterialResponse>
{
    private readonly IPackingMaterialRepository _repository;

    public DeletePackingMaterialHandler(IPackingMaterialRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeletePackingMaterialResponse> Handle(DeletePackingMaterialRequest request, CancellationToken cancellationToken)
    {
        var packingMaterial = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (packingMaterial == null)
        {
            return new DeletePackingMaterialResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ResourceNotFound,
                Error = $"Packing material with ID {request.Id} not found."
            };
        }

        await _repository.DeleteAsync(packingMaterial, cancellationToken);
        return new DeletePackingMaterialResponse();
    }
}