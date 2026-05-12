using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetAllocations;

public class GetAllocationsRequest : IRequest<GetAllocationsResponse>
{
    public int PackingMaterialId { get; set; }
}
