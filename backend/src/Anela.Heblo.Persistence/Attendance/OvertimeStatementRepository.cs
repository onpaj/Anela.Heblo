using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Attendance;

public class OvertimeStatementRepository : IOvertimeStatementRepository
{
    private readonly ApplicationDbContext _context;

    public OvertimeStatementRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<OvertimeMonthlyStatement>> GetByMonthAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeMonthlyStatements
            .Where(s => s.Year == year && s.Month == month)
            .ToListAsync(cancellationToken);
    }

    public async Task<OvertimeMonthlyStatement?> GetLatestClosedAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeMonthlyStatements
            .Where(s => s.PersonId == personId && s.Status == OvertimeStatementStatus.Closed)
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OvertimeMonthlyStatement>> GetAllClosedAsync(CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeMonthlyStatements
            .Where(s => s.Status == OvertimeStatementStatus.Closed)
            .OrderBy(s => s.Year).ThenBy(s => s.Month)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyOpenBeforeAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeMonthlyStatements
            .AnyAsync(s => s.Status == OvertimeStatementStatus.Open
                           && (s.Year < year || (s.Year == year && s.Month < month)), cancellationToken);
    }

    public async Task<IReadOnlyList<(int Year, int Month)>> GetClosedMonthsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _context.OvertimeMonthlyStatements
            .Where(s => s.Status == OvertimeStatementStatus.Closed)
            .Select(s => new { s.Year, s.Month })
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.Year, r.Month)).ToList();
    }

    public async Task AddAsync(OvertimeMonthlyStatement statement, CancellationToken cancellationToken = default)
    {
        var entry = _context.OvertimeMonthlyStatements.Add(statement);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A failed INSERT leaves the entity tracked as Added; detach it so it cannot
            // be re-attempted by a later SaveChanges on the shared scoped DbContext.
            entry.State = EntityState.Detached;
            throw;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
