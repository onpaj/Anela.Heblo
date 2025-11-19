using Anela.Heblo.Domain.Features.PackingMaterials;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;

public class DeletePackingMaterialHandler : IRequestHandler<DeletePackingMaterialRequest>
{
    private readonly IPackingMaterialRepository _repository;

    public DeletePackingMaterialHandler(IPackingMaterialRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(DeletePackingMaterialRequest request, CancellationToken cancellationToken)
    {
        var packingMaterial = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (packingMaterial == null)
        {
            throw new InvalidOperationException($"Packing material with ID {request.Id} not found.");
        }

        await _repository.DeleteAsync(packingMaterial, cancellationToken);
    }
}