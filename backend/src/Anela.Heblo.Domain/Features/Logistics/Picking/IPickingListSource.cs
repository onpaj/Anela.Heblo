namespace Anela.Heblo.Domain.Features.Logistics.Picking;

public interface IPickingListSource
{
    Task<PrintPickingListResult> CreatePickingList(PrintPickingListRequest request, CancellationToken cancellationToken = default);
}