using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreateAllocation;

public class CreateAllocationResponse : BaseResponse
{
    public string? Error { get; set; }
    public PackingMaterialAllocationDto? Allocation { get; set; }
}
