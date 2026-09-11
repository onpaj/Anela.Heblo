using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class TransportBoxInventoryRestorer : ITransportBoxInventoryRestorer
{
    private readonly IInventoryReservationService _inventoryReservationService;

    public TransportBoxInventoryRestorer(IInventoryReservationService inventoryReservationService)
    {
        _inventoryReservationService = inventoryReservationService;
    }

    public async Task RestoreAsync(
        IReadOnlyList<TransportBoxItem> items,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (item.SourceInventoryId == null) continue;

            await _inventoryReservationService.RestoreAsync(
                inventoryId: item.SourceInventoryId.Value,
                amount: (decimal)item.Amount,
                userName: userName,
                timestamp: timestamp,
                boxId: boxId,
                boxCode: boxCode,
                cancellationToken: cancellationToken);
        }
    }
}
