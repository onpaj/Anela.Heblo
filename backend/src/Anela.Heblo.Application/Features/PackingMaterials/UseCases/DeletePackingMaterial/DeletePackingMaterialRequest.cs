using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;

public class DeletePackingMaterialRequest : IRequest
{
    public int Id { get; set; }
}