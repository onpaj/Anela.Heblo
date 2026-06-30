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
        // Npgsql rejects DateTime with Kind != Utc on 'timestamptz' columns (Npgsql 6+).
        // Although GridLayouts.LastModified maps to 'timestamp' (without time zone),
        // GetUtcNow().UtcDateTime is used here for semantic correctness and to be safe
        // if the column type is ever changed to 'timestamptz'.
        var lastModified = _timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            // Raw SQL: column list must match the EF mapping in GridLayoutConfiguration.
            // See memory/gotchas/raw-sql-insert-must-match-ef-mapping.md when adding columns.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO public.""GridLayouts"" (""UserId"", ""GridKey"", ""LayoutJson"", ""LastModified"")
                   VALUES ({userId}, {gridKey}, {layoutJson}, {lastModified})
                   ON CONFLICT (""UserId"", ""GridKey"") DO UPDATE
                      SET ""LayoutJson""   = EXCLUDED.""LayoutJson"",
                          ""LastModified"" = EXCLUDED.""LastModified""",
                cancellationToken);
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

    // DeleteAsync stays on the EF read-then-write path: a delete/delete race is benign (idempotent), so there is no defect to fix here.
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
