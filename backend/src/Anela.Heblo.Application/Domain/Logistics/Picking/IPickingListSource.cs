namespace Anela.Heblo.Application.Domain.Logistics.Picking;

public interface IPickingListSource
{
    Task<PrintPickingListResult> CreatePickingList(PrintPickingListRequest request, CancellationToken cancellationToken = default);
}