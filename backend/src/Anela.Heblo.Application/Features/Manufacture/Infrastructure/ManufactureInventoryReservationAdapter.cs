using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Infrastructure;

/// <summary>
/// Manufacture-owned adapter that implements the Logistics-owned
/// <see cref="IInventoryReservationService"/> contract by delegating to the Manufacture
/// inventory repository and domain methods.
///
/// Does not commit — the caller's unit of work (the transport-box repository's
/// SaveChangesAsync) commits both mutations atomically against the shared
/// ApplicationDbContext.
/// </summary>
internal sealed class ManufactureInventoryReservationAdapter : IInventoryReservationService
{
    private readonly IManufacturedProductInventoryRepository _inventoryRepository;
    private readonly ILogger<ManufactureInventoryReservationAdapter> _logger;

    public ManufactureInventoryReservationAdapter(
        IManufacturedProductInventoryRepository inventoryRepository,
        ILogger<ManufactureInventoryReservationAdapter> logger)
    {
        _inventoryRepository = inventoryRepository;
        _logger = logger;
    }

    public async Task<ConsumeInventoryResult> TryConsumeAsync(
        int inventoryId,
        decimal amount,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        bool allowNegativeStock,
        CancellationToken cancellationToken)
    {
        var item = await _inventoryRepository.GetByIdAsync(inventoryId, cancellationToken);
        if (item is null)
        {
            return new ConsumeInventoryResult(ConsumeInventoryOutcome.InventoryNotFound);
        }

        try
        {
            // ManufacturedProductInventoryItem.Consume is the sole producer of
            // InvalidOperationException on this call path (insufficient stock guard at
            // Domain/Features/Manufacture/Inventory/ManufacturedProductInventoryItem.cs:55-57).
            // If a future invariant adds another InvalidOperationException here, this catch
            // would miscategorize it — track upgrade to a typed InsufficientInventoryException
            // as a follow-up.
            item.Consume(amount, userName, timestamp, boxId, boxCode, allowNegativeStock);
        }
        catch (InvalidOperationException)
        {
            return new ConsumeInventoryResult(ConsumeInventoryOutcome.InsufficientStock);
        }

        await _inventoryRepository.UpdateAsync(item, cancellationToken);
        return new ConsumeInventoryResult(ConsumeInventoryOutcome.Success);
    }

    public async Task RestoreAsync(
        int inventoryId,
        decimal amount,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        CancellationToken cancellationToken)
    {
        var item = await _inventoryRepository.GetByIdAsync(inventoryId, cancellationToken);
        if (item is null)
        {
            _logger.LogWarning(
                "InventoryItem {InventoryId} not found during restore for transport box {BoxId} — skipping restore",
                inventoryId, boxId);
            return;
        }

        item.Restore(amount, userName, timestamp, boxId, boxCode);
        await _inventoryRepository.UpdateAsync(item, cancellationToken);
    }
}
