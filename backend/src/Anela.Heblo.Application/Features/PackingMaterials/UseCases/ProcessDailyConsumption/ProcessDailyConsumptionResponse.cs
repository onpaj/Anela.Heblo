using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;

public class ProcessDailyConsumptionResponse : BaseResponse
{
    public DateOnly ProcessedDate { get; set; }
    public int MaterialsProcessed { get; set; }
    public string Message { get; set; } = string.Empty;
}