using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;

public class GetDailyConsumptionBreakdownResponse : BaseResponse
{
    public string? Error { get; set; }
    public DateOnly Date { get; set; }
    public string GroupBy { get; set; } = string.Empty;
    public List<ConsumptionGroupDto> Groups { get; set; } = new();
}
