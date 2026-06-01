using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;

public class GetDailyConsumptionBreakdownRequest : IRequest<GetDailyConsumptionBreakdownResponse>
{
    public DateOnly Date { get; set; }
    public string GroupBy { get; set; } = "material";
}
