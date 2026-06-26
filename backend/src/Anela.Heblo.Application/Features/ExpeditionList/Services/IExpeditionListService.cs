using Anela.Heblo.Application.Features.ExpeditionList.Contracts;

namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

public interface IExpeditionListService
{
    Task<ExpeditionPickingResult> PrintPickingListAsync(
        ExpeditionPickingRequest request,
        IList<string>? emailList = null,
        CancellationToken cancellationToken = default);
}
