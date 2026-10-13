namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

/// <summary>
/// Runs right before a picking list is built so the source orders are in printable shape
/// (today: the double-shot V → S swap, implemented by the ShoptetOrders module).
/// A failure must never stop printing; ExpeditionListService logs and continues.
/// </summary>
public interface IPickingListOrderPreparer
{
    Task PrepareAsync(CancellationToken cancellationToken = default);
}
