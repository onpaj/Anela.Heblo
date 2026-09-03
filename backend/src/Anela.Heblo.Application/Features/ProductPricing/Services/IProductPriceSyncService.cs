namespace Anela.Heblo.Application.Features.ProductPricing.Services;

public interface IProductPriceSyncService
{
    Task<PriceSyncRunResult> SyncAsync(CancellationToken ct);
}
