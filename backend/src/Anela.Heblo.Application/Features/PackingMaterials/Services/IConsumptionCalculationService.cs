using Anela.Heblo.Domain.Features.PackingMaterials;

namespace Anela.Heblo.Application.Features.PackingMaterials.Services;

public interface IConsumptionCalculationService
{
    Task<decimal> CalculateConsumptionAsync(
        PackingMaterial material,
        int orderCount,
        int productCount);

    Task<bool> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        int orderCount,
        int productCount,
        CancellationToken cancellationToken = default);

    Task<bool> HasDayAlreadyBeenProcessedAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}