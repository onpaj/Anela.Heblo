using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdateAllocation;

public class UpdateAllocationRequest : IRequest<UpdateAllocationResponse>
{
    public int PackingMaterialId { get; set; }
    public int AllocationId { get; set; }
    public string ProductCode { get; set; } = null!;
    public decimal AmountPerUnit { get; set; }
}
