using Anela.Heblo.Domain.Features.Logistics.Picking;

namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

public interface IExpeditionListService
{
    Task<PrintPickingListResult> PrintPickingListAsync(
        PrintPickingListRequest request,
        IList<string>? emailList = null,
        CancellationToken cancellationToken = default);
}
