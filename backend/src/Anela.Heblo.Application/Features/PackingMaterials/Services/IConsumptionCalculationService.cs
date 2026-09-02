namespace Anela.Heblo.Application.Features.PackingMaterials.Services;

public interface IConsumptionCalculationService
{
    Task<ProcessDailyConsumptionResult> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        CancellationToken cancellationToken = default);
}
