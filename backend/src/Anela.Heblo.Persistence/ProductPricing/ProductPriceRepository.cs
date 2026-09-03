using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.ProductPricing;

public class ProductPriceRepository : IProductPriceRepository
{
    private readonly ApplicationDbContext _context;

    public ProductPriceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ProductPrice>> GetAllAsync(CancellationToken ct) =>
        await _context.ProductPrices.ToListAsync(ct);

    public async Task<ProductPrice?> GetAsync(string productCode, CancellationToken ct) =>
        await _context.ProductPrices.FirstOrDefaultAsync(p => p.Id == productCode, ct);

    public async Task UpsertAsync(ProductPrice price, CancellationToken ct)
    {
        var existing = await _context.ProductPrices.FirstOrDefaultAsync(p => p.Id == price.Id, ct);
        if (existing is null)
        {
            _context.ProductPrices.Add(price);
            return;
        }

        existing.PriceWithVat = price.PriceWithVat;
        existing.VatRate = price.VatRate;
        existing.ModifiedAt = price.ModifiedAt;
        existing.ModifiedBy = price.ModifiedBy;
    }

    public async Task<IReadOnlyList<ProductPriceSyncState>> GetSyncStatesAsync(
        PriceSyncTarget target, CancellationToken ct) =>
        await _context.ProductPriceSyncStates.Where(s => s.Target == target).ToListAsync(ct);

    public async Task<IReadOnlyList<ProductPriceSyncState>> GetConflictsAsync(CancellationToken ct) =>
        await _context.ProductPriceSyncStates
            .Where(s => s.Status == PriceSyncStatus.Conflict)
            .ToListAsync(ct);

    public async Task<ProductPriceSyncState?> GetSyncStateAsync(
        string productCode, PriceSyncTarget target, CancellationToken ct) =>
        await _context.ProductPriceSyncStates
            .FirstOrDefaultAsync(s => s.ProductCode == productCode && s.Target == target, ct);

    public async Task UpsertSyncStateAsync(ProductPriceSyncState state, CancellationToken ct)
    {
        var existing = await GetSyncStateAsync(state.ProductCode, state.Target, ct);
        if (existing is null)
        {
            _context.ProductPriceSyncStates.Add(state);
            return;
        }

        existing.LastPushedPriceWithVat = state.LastPushedPriceWithVat;
        existing.LastPushedAt = state.LastPushedAt;
        existing.Status = state.Status;
        existing.RemoteValueAtConflict = state.RemoteValueAtConflict;
        existing.ConflictDetectedAt = state.ConflictDetectedAt;
        existing.LastError = state.LastError;
    }

    public Task SaveChangesAsync(CancellationToken ct) => _context.SaveChangesAsync(ct);
}
