using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.GridLayouts;

public class GridLayoutRepository : IGridLayoutRepository
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly PostgresExceptionTranslator _translator;

    public GridLayoutRepository(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        PostgresExceptionTranslator translator)
    {
        _context = context;
        _timeProvider = timeProvider;
        _translator = translator;
    }

    public async Task<GridLayout?> GetAsync(string userId, string gridKey, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.GridLayouts
                .FirstOrDefaultAsync(x => x.UserId == userId && x.GridKey == gridKey, cancellationToken);
        }
        catch (Exception ex)
        {
            var translated = _translator.TryTranslateGridLayout(ex, nameof(GetAsync));
            if (translated is not null)
            {
                throw translated;
            }
            throw;
        }
    }

    public async Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _context.GridLayouts
                .FirstOrDefaultAsync(x => x.UserId == userId && x.GridKey == gridKey, cancellationToken);

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
        catch (Exception ex)
        {
            var translated = _translator.TryTranslateGridLayout(ex, nameof(UpsertAsync));
            if (translated is not null)
            {
                throw translated;
            }
            throw;
        }
    }

    public async Task DeleteAsync(string userId, string gridKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _context.GridLayouts
                .FirstOrDefaultAsync(x => x.UserId == userId && x.GridKey == gridKey, cancellationToken);

            if (existing is not null)
            {
                _context.GridLayouts.Remove(existing);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            var translated = _translator.TryTranslateGridLayout(ex, nameof(DeleteAsync));
            if (translated is not null)
            {
                throw translated;
            }
            throw;
        }
    }
}
