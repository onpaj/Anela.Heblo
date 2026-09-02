using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;

public class GetDailyConsumptionBreakdownRequest : IRequest<GetDailyConsumptionBreakdownResponse>
{
    public DateOnly Date { get; set; }
    public ConsumptionGroupBy GroupBy { get; set; } = ConsumptionGroupBy.Material;
}
