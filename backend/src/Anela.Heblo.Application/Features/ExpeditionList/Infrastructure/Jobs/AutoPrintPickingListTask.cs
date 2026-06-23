using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;

namespace Anela.Heblo.Application.Features.ExpeditionList.Infrastructure.Jobs;

/// <summary>
/// Single print pass for the "Tisk-Robot" source state — the unattended counterpart to the manual
/// "Spustit tisk oprav" button. Reuses the same <see cref="IExpeditionListService.PrintPickingListAsync"/>
/// flow as the manual fix and the daily job, only with a different source state. Scheduling, enable/disable,
/// visibility and manual triggering are provided by the BackgroundRefresh task registry (registered in
/// <c>ExpeditionListModule</c>).
/// </summary>
public static class AutoPrintPickingListTask
{
    /// <summary>
    /// Runs a single print pass for the Tisk-Robot source state. Printer-only (no email copies).
    /// </summary>
    public static async Task<int> ExecuteOnceAsync(
        IExpeditionListService service,
        PrintPickingListOptions options,
        CancellationToken cancellationToken)
    {
        var request = new ExpeditionPickingRequest
        {
            Carriers = ExpeditionPickingRequest.DefaultCarriers,
            SourceStateId = options.AutoPrintSourceStateId,
            DesiredStateId = options.DesiredStateId,
            ChangeOrderState = options.ChangeOrderStateByDefault,
            SendToPrinter = options.SendToPrinterByDefault,
        };

        var result = await service.PrintPickingListAsync(request, cancellationToken: cancellationToken);

        return result.TotalCount;
    }
}
