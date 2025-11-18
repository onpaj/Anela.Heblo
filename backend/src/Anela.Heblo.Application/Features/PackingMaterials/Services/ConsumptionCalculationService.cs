using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.Services;

public class ConsumptionCalculationService : IConsumptionCalculationService
{
    private readonly IPackingMaterialRepository _repository;
    private readonly ILogger<ConsumptionCalculationService> _logger;

    public ConsumptionCalculationService(
        IPackingMaterialRepository repository,
        ILogger<ConsumptionCalculationService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task<decimal> CalculateConsumptionAsync(
        PackingMaterial material,
        int orderCount,
        int productCount)
    {
        var consumption = material.ConsumptionType switch
        {
            ConsumptionType.PerOrder => material.ConsumptionRate * orderCount,
            ConsumptionType.PerProduct => material.ConsumptionRate * productCount,
            ConsumptionType.PerDay => material.ConsumptionRate, // Fixed amount per day
            _ => throw new ArgumentOutOfRangeException(nameof(material.ConsumptionType))
        };

        _logger.LogDebug("Calculated consumption for material {MaterialId} ({MaterialName}): {Consumption} (Type: {Type}, Rate: {Rate}, Orders: {Orders}, Products: {Products})",
            material.Id, material.Name, consumption, material.ConsumptionType, material.ConsumptionRate, orderCount, productCount);

        return Task.FromResult(consumption);
    }

    public async Task<bool> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        int orderCount,
        int productCount,
        CancellationToken cancellationToken = default)
    {
        // Check if day already processed to prevent duplicates
        if (await HasDayAlreadyBeenProcessedAsync(processingDate, cancellationToken))
        {
            _logger.LogInformation("Daily consumption processing for {Date} already completed, skipping", processingDate);
            return false;
        }

        _logger.LogInformation("Starting daily consumption processing for {Date} with {OrderCount} orders and {ProductCount} products",
            processingDate, orderCount, productCount);

        // Get all active materials
        var materials = await _repository.GetAllAsync(cancellationToken);
        var processedCount = 0;

        foreach (var material in materials)
        {
            try
            {
                var consumptionAmount = await CalculateConsumptionAsync(material, orderCount, productCount);

                if (consumptionAmount > 0)
                {
                    // Calculate new quantity after consumption
                    var newQuantity = Math.Max(0, material.CurrentQuantity - consumptionAmount);

                    // Update quantity and log the consumption
                    material.UpdateQuantity(newQuantity, processingDate, LogEntryType.AutomaticConsumption);

                    await _repository.UpdateAsync(material, cancellationToken);
                    processedCount++;

                    _logger.LogInformation("Processed material {MaterialName}: consumed {Consumption}, new quantity: {NewQuantity}",
                        material.Name, consumptionAmount, newQuantity);
                }
                else
                {
                    _logger.LogDebug("Skipping material {MaterialName}: no consumption calculated", material.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing material {MaterialId} ({MaterialName}) for date {Date}",
                    material.Id, material.Name, processingDate);
            }
        }

        _logger.LogInformation("Completed daily consumption processing for {Date}. Processed {ProcessedCount} materials",
            processingDate, processedCount);

        return true;
    }

    public async Task<bool> HasDayAlreadyBeenProcessedAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await _repository.HasDailyProcessingBeenRunAsync(date, cancellationToken);
    }
}