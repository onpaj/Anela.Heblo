using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetAllocations;

public class GetAllocationsResponse : BaseResponse
{
    public List<PackingMaterialAllocationDto> Allocations { get; set; } = new();
    public string? Error { get; set; }
}
