using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreateAllocation;

public class CreateAllocationRequest : IRequest<CreateAllocationResponse>
{
    public int PackingMaterialId { get; set; }
    public string ProductCode { get; set; } = null!;
    public decimal AmountPerUnit { get; set; }
}
