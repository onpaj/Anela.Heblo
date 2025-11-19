using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;

public class ProcessDailyConsumptionRequest : IRequest<ProcessDailyConsumptionResponse>
{
    public DateOnly ProcessingDate { get; set; }
}