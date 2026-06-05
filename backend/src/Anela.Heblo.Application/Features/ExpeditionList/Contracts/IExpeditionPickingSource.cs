namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface IExpeditionPickingSource
{
    Task<ExpeditionPickingResult> CreatePickingListAsync(
        ExpeditionPickingRequest request,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken = default);
}
