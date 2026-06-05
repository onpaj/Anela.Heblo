using Anela.Heblo.Domain.Features.FeatureFlags;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.FeatureFlags;

public sealed class FeatureFlagOverrideRepository : IFeatureFlagOverrideRepository
{
    private readonly ApplicationDbContext _ctx;

    public FeatureFlagOverrideRepository(ApplicationDbContext ctx) => _ctx = ctx;

    public async Task<Dictionary<string, bool>> GetAllAsDictionaryAsync(CancellationToken ct = default)
        => await _ctx.FeatureFlagOverrides
            .AsNoTracking()
            .ToDictionaryAsync(e => e.Key, e => e.IsEnabled, ct);

    public async Task<bool?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        var entity = await _ctx.FeatureFlagOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Key == key, ct);
        return entity?.IsEnabled;
    }

    public async Task<IReadOnlyList<FeatureFlagOverride>> GetAllAsync(CancellationToken ct = default)
        => await _ctx.FeatureFlagOverrides.AsNoTracking().ToListAsync(ct);

    public async Task UpsertAsync(string key, bool isEnabled, string updatedBy, CancellationToken ct = default)
    {
        var existing = await _ctx.FeatureFlagOverrides.FindAsync([key], ct);
        if (existing is not null)
        {
            existing.IsEnabled = isEnabled;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = updatedBy;
        }
        else
        {
            _ctx.FeatureFlagOverrides.Add(new FeatureFlagOverride
            {
                Key = key,
                IsEnabled = isEnabled,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = updatedBy,
            });
        }
        await _ctx.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        var existing = await _ctx.FeatureFlagOverrides.FindAsync([key], ct);
        if (existing is null) return false;
        _ctx.FeatureFlagOverrides.Remove(existing);
        await _ctx.SaveChangesAsync(ct);
        return true;
    }
}
