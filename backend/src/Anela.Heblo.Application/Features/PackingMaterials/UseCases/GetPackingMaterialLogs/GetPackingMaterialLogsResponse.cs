using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;

public class GetPackingMaterialLogsResponse : BaseResponse
{
    public PackingMaterialDto Material { get; set; } = null!;
    public IEnumerable<PackingMaterialLogDto> Logs { get; set; } = new List<PackingMaterialLogDto>();
}