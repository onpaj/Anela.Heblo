namespace Anela.Heblo.Domain.Features.Logistics.Transport;

/// <summary>
/// Outcome of decreasing a transport box item's amount: the affected item, how much was actually
/// removed, and whether the row was removed entirely (amount reached zero).
/// </summary>
public record DecreaseItemResult(TransportBoxItem Item, double RemovedAmount, bool RowRemoved);
