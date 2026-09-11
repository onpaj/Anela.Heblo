namespace Anela.Heblo.Domain.Features.ProductPricing;

public interface IProductPriceRepository
{
    Task<IReadOnlyList<ProductPrice>> GetAllAsync(CancellationToken ct);
    Task<ProductPrice?> GetAsync(string productCode, CancellationToken ct);
    Task UpsertAsync(ProductPrice price, CancellationToken ct);
    Task<IReadOnlyList<ProductPriceSyncState>> GetSyncStatesAsync(PriceSyncTarget target, CancellationToken ct);
    Task<IReadOnlyList<ProductPriceSyncState>> GetConflictsAsync(CancellationToken ct);
    Task<ProductPriceSyncState?> GetSyncStateAsync(string productCode, PriceSyncTarget target, CancellationToken ct);
    Task UpsertSyncStateAsync(ProductPriceSyncState state, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
