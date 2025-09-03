using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Persistence.Repositories;

public class ManufactureDifficultyRepository : BaseRepository<ManufactureDifficultySetting, int>, IManufactureDifficultyRepository
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ManufactureDifficultyRepository> _logger;

    public ManufactureDifficultyRepository(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        ILogger<ManufactureDifficultyRepository> logger)
        : base(context)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<List<ManufactureDifficultySetting>> ListAsync(string? productCode = null, DateTime? asOfDate = null, CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsQueryable();

        if (asOfDate.HasValue)
        {
            query = query.Where(h => (h.ValidFrom == null || h.ValidFrom <= asOfDate) &&
                                   (h.ValidTo == null || h.ValidTo >= asOfDate));
        }

        if (productCode != null)
        {
            query = query.Where(h => h.ProductCode == productCode);
        }

        var result = await query
            .OrderBy(h => h.ProductCode)
            .ThenByDescending(h => h.ValidFrom ?? DateTime.MinValue)
            .ToListAsync(cancellationToken);

        _logger.LogDebug("Retrieved {Count} manufacture difficulty history records as of {AsOfDate}",
            result.Count, asOfDate);

        return result;
    }

    public async Task<ManufactureDifficultySetting?> FindAsync(string productCode, DateTime asOfDate, CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(h => h.ProductCode == productCode);

        query = query.Where(h => (h.ValidFrom == null || h.ValidFrom <= asOfDate) &&
                               (h.ValidTo == null || h.ValidTo >= asOfDate));

        var result = await query
            .OrderByDescending(h => h.ValidFrom ?? DateTime.MinValue)
            .FirstOrDefaultAsync(cancellationToken);

        return result;
    }

    public async Task<ManufactureDifficultySetting> CreateAsync(ManufactureDifficultySetting history, CancellationToken cancellationToken = default)
    {
        history.CreatedAt = DateTime.UtcNow;
        var result = await AddAsync(history, cancellationToken);
        await SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Created manufacture difficulty history {Id} for product {ProductCode}",
            result.Id, result.ProductCode);

        return result;
    }

    public new async Task<ManufactureDifficultySetting> UpdateAsync(ManufactureDifficultySetting history, CancellationToken cancellationToken = default)
    {
        await base.UpdateAsync(history, cancellationToken);
        await SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Updated manufacture difficulty history {Id} for product {ProductCode}",
            history.Id, history.ProductCode);

        return history;
    }

    public new async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await base.DeleteAsync(id, cancellationToken);
        await SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Deleted manufacture difficulty history {Id}", id);
    }

    public async Task<bool> HasOverlapAsync(string productCode, DateTime? validFrom, DateTime? validTo, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(h => h.ProductCode == productCode);

        if (excludeId.HasValue)
        {
            query = query.Where(h => h.Id != excludeId.Value);
        }

        // Check for overlaps:
        // 1. New period starts before existing ends (or existing has no end)
        // 2. New period ends after existing starts (or existing has no start)
        var hasOverlap = await query.AnyAsync(h => 
            (validFrom == null || h.ValidTo == null || validFrom <= h.ValidTo) &&
            (validTo == null || h.ValidFrom == null || validTo >= h.ValidFrom),
            cancellationToken);

        _logger.LogDebug("Checked overlap for product {ProductCode} from {ValidFrom} to {ValidTo}, excluding {ExcludeId}: {HasOverlap}",
            productCode, validFrom, validTo, excludeId, hasOverlap);

        return hasOverlap;
    }
}