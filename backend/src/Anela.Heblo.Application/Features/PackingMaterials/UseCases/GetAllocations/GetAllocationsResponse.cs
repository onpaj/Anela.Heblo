using Anela.Heblo.Application.Features.PackingMaterials.Contracts;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetAllocations;

public class GetAllocationsResponse
{
    public bool Success { get; set; }
    public List<PackingMaterialAllocationDto> Allocations { get; set; } = new();
    public string? Error { get; set; }
}
