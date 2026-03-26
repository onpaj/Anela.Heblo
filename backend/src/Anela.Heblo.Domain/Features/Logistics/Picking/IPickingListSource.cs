namespace Anela.Heblo.Domain.Features.Logistics.Picking;

public interface IPickingListSource
{
    Task<PrintPickingListResult> CreatePickingList(PrintPickingListRequest request, CancellationToken cancellationToken = default);
    Task ChangeOrderState(IList<int> orderIds, int sourceStateId, int desiredStateId, CancellationToken cancellationToken = default);
}