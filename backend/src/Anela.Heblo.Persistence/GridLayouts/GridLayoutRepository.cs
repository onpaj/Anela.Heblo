using Anela.Heblo.Domain.Features.GridLayouts;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.GridLayouts;

public class GridLayoutRepository : IGridLayoutRepository
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public GridLayoutRepository(ApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<GridLayout?> GetAsync(string userId, string gridKey, CancellationToken cancellationToken = default)
    {
        return await _context.GridLayouts
            .FirstOrDefaultAsync(x => x.UserId == userId && x.GridKey == gridKey, cancellationToken);
    }

    public async Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(userId, gridKey, cancellationToken);

        if (existing is not null)
        {
            existing.LayoutJson = layoutJson;
            existing.LastModified = _timeProvider.GetUtcNow().DateTime;
        }
        else
        {
            _context.GridLayouts.Add(new GridLayout
            {
                UserId = userId,
                GridKey = gridKey,
                LayoutJson = layoutJson,
                LastModified = _timeProvider.GetUtcNow().DateTime
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string userId, string gridKey, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(userId, gridKey, cancellationToken);
        if (existing is not null)
        {
            _context.GridLayouts.Remove(existing);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
