using Anela.Heblo.Application.Features.PackingMaterials.Contracts;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;

public class GetPackingMaterialLogsResponse
{
    public PackingMaterialDto Material { get; set; } = null!;
    public IEnumerable<PackingMaterialLogDto> Logs { get; set; } = new List<PackingMaterialLogDto>();
}