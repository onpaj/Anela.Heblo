using Anela.Heblo.Application.Features.PackingMaterials.Contracts;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;

public class GetConsumptionHistoryResponse
{
    public List<MaterialConsumptionHistoryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}
