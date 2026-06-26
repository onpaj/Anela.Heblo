using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;

public class DeletePackingMaterialRequest : IRequest<DeletePackingMaterialResponse>
{
    public int Id { get; set; }
}