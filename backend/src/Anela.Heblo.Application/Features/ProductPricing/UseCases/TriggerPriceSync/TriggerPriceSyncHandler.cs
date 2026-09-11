using Anela.Heblo.Application.Features.ProductPricing.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.TriggerPriceSync;

public class TriggerPriceSyncHandler : IRequestHandler<TriggerPriceSyncRequest, TriggerPriceSyncResponse>
{
    private readonly IProductPriceSyncService _syncService;

    public TriggerPriceSyncHandler(IProductPriceSyncService syncService)
    {
        _syncService = syncService;
    }

    public async Task<TriggerPriceSyncResponse> Handle(
        TriggerPriceSyncRequest request, CancellationToken cancellationToken)
    {
        var result = await _syncService.SyncAsync(cancellationToken);

        return new TriggerPriceSyncResponse
        {
            Pushed = result.Pushed,
            Conflicts = result.Conflicts,
            Failed = result.Failed,
            Seeded = result.Seeded,
            Unchanged = result.Unchanged,
        };
    }
}
