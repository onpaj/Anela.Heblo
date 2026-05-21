using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.Services;

public class ConsumptionCalculationService : IConsumptionCalculationService
{
    private readonly IPackingMaterialRepository _repository;
    private readonly IInvoiceConsumptionSource _invoiceSource;
    private readonly ILogger<ConsumptionCalculationService> _logger;

    public ConsumptionCalculationService(
        IPackingMaterialRepository repository,
        IInvoiceConsumptionSource invoiceSource,
        ILogger<ConsumptionCalculationService> logger)
    {
        _repository = repository;
        _invoiceSource = invoiceSource;
        _logger = logger;
    }

    public async Task<ProcessDailyConsumptionResult> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        CancellationToken cancellationToken = default)
    {
        if (await HasDayAlreadyBeenProcessedAsync(processingDate, cancellationToken))
        {
            _logger.LogInformation("Daily consumption processing for {Date} already completed, skipping", processingDate);
            return new ProcessDailyConsumptionResult(false, 0);
        }

        _logger.LogInformation("Starting daily consumption processing for {Date}", processingDate);

        var materials = (await _repository.GetAllWithAllocationsAsync(cancellationToken)).ToList();
        var invoices = await _invoiceSource.GetHeadersByDateAsync(processingDate, cancellationToken);

        var allFactRows = new List<PackingMaterialConsumption>();
        var decrementByMaterial = new Dictionary<PackingMaterial, decimal>();

        var processedCount = 0;

        foreach (var material in materials)
        {
            var rows = BuildFactRows(material, invoices, processingDate);
            var total = rows.Sum(r => r.Amount);

            if (total > 0)
            {
                allFactRows.AddRange(rows);
                decrementByMaterial[material] = total;
                processedCount++;
            }
        }

        foreach (var material in materials)
        {
            if (decrementByMaterial.TryGetValue(material, out var decrement))
            {
                var newQuantity = Math.Max(0, material.CurrentQuantity - decrement);
                material.UpdateQuantity(newQuantity, processingDate, LogEntryType.AutomaticConsumption);

                _logger.LogInformation("Processed material {MaterialName}: consumed {Consumption}, new quantity: {NewQuantity}",
                    material.Name, decrement, newQuantity);
            }
        }

        // Relies on EF change tracking — GetAllWithAllocationsAsync must NOT use AsNoTracking
        if (processedCount == 0 && materials.Count > 0)
        {
            var marker = materials[0];
            marker.UpdateQuantity(marker.CurrentQuantity, processingDate, LogEntryType.AutomaticConsumption);
        }

        if (allFactRows.Count > 0)
            await _repository.AddConsumptionRowsAsync(allFactRows, cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Completed daily consumption processing for {Date}. Processed {ProcessedCount} materials",
            processingDate, processedCount);

        return new ProcessDailyConsumptionResult(true, processedCount);
    }

    public async Task<bool> HasDayAlreadyBeenProcessedAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await _repository.HasDailyProcessingBeenRunAsync(date, cancellationToken);
    }

    private static List<PackingMaterialConsumption> BuildFactRows(
        PackingMaterial material,
        IReadOnlyList<InvoiceConsumptionHeader> invoices,
        DateOnly date)
    {
        return material.ConsumptionType switch
        {
            ConsumptionType.PerDay => new List<PackingMaterialConsumption>
            {
                new PackingMaterialConsumption(material.Id, date, ConsumptionType.PerDay, material.ConsumptionRate)
            },
            ConsumptionType.PerOrder => invoices
                .Select(inv => new PackingMaterialConsumption(
                    material.Id, date, ConsumptionType.PerOrder, material.ConsumptionRate, inv.Id))
                .ToList(),
            ConsumptionType.PerProduct => invoices
                .Select(inv => new PackingMaterialConsumption(
                    material.Id, date, ConsumptionType.PerProduct, material.ConsumptionRate * inv.ItemsCount, inv.Id))
                .Where(r => r.Amount > 0)
                .ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(material.ConsumptionType))
        };
    }
}
