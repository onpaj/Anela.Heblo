using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ManufactureInventoryWriteDownService : IManufactureInventoryWriteDownService
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ManufactureInventoryWriteDownService> _logger;
    private readonly IManufacturedProductInventoryRepository _inventoryRepository;
    private readonly IManufactureCatalogSource _catalogSource;

    public ManufactureInventoryWriteDownService(
        TimeProvider timeProvider,
        ILogger<ManufactureInventoryWriteDownService> logger,
        IManufacturedProductInventoryRepository inventoryRepository,
        IManufactureCatalogSource catalogSource)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _inventoryRepository = inventoryRepository;
        _catalogSource = catalogSource;
    }

    public async Task WriteDownAsync(ManufactureOrder order, string changedByUser, CancellationToken cancellationToken)
    {
        var timestamp = _timeProvider.GetUtcNow().DateTime;

        var productsWithQuantity = order.Products
            .Where(p => p.ActualQuantity is > 0)
            .ToList();

        if (productsWithQuantity.Count == 0)
            return;

        var productCodes = productsWithQuantity.Select(p => p.ProductCode).Distinct().ToList();
        var catalogEntries = await _catalogSource.GetByIdsAsync(productCodes, cancellationToken);

        // Exclude semi-products, then aggregate by product+lot+expiration so repeated output of the same
        // product/lot produces a single inventory row instead of duplicates.
        var lines = productsWithQuantity
            .Where(p => !catalogEntries.TryGetValue(p.ProductCode, out var entry) || entry.Type != ProductType.SemiProduct)
            .GroupBy(p => new { p.ProductCode, p.LotNumber, p.ExpirationDate })
            .Select(g => new
            {
                g.Key.ProductCode,
                ProductName = g.First().ProductName,
                g.Key.LotNumber,
                g.Key.ExpirationDate,
                Amount = g.Sum(p => p.ActualQuantity!.Value)
            })
            .ToList();

        if (lines.Count == 0)
            return;

        var existingItems = await _inventoryRepository.GetByProductCodesWithLogsAsync(
            lines.Select(l => l.ProductCode).Distinct().ToList(), cancellationToken);

        var newItems = new List<ManufacturedProductInventoryItem>();

        foreach (var line in lines)
        {
            var existing = existingItems.FirstOrDefault(i =>
                i.ProductCode == line.ProductCode
                && i.LotNumber == line.LotNumber
                && i.ExpirationDate == line.ExpirationDate);

            if (existing != null)
            {
                // Idempotency: completing the same order again must not write the amount twice.
                if (existing.WasWrittenDownByOrder(order.Id))
                {
                    _logger.LogInformation(
                        "Skipping duplicate inventory write-down for order {OrderId}, product {ProductCode}, lot {LotNumber} — already written",
                        order.Id, line.ProductCode, line.LotNumber);
                    continue;
                }

                existing.WriteDownFromManufacture(line.Amount, changedByUser, timestamp, order.Id);
            }
            else
            {
                newItems.Add(new ManufacturedProductInventoryItem(
                    productCode: line.ProductCode,
                    productName: line.ProductName,
                    amount: line.Amount,
                    createdBy: changedByUser,
                    createdAt: timestamp,
                    lotNumber: line.LotNumber,
                    expirationDate: line.ExpirationDate,
                    manufactureOrderId: order.Id));
            }
        }

        if (newItems.Count > 0)
            await _inventoryRepository.AddRangeAsync(newItems, cancellationToken);
    }
}
