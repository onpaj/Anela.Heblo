using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeleteAllocation;

public class DeleteAllocationRequest : IRequest<DeleteAllocationResponse>
{
    public int PackingMaterialId { get; set; }
    public int AllocationId { get; set; }
}
