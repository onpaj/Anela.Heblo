using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public interface ITransportBoxInventoryRestorer
{
    Task RestoreAsync(
        IReadOnlyList<TransportBoxItem> items,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        CancellationToken cancellationToken);
}
