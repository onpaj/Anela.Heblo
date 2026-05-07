using Anela.Heblo.Application.Features.PackingMaterials.Contracts;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreateAllocation;

public class CreateAllocationResponse
{
    public bool Success { get; set; }
    public PackingMaterialAllocationDto? Allocation { get; set; }
    public string? Error { get; set; }
}
