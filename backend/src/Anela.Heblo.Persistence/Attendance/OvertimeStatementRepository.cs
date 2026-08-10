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

    public async Task AddAsync(OvertimeMonthlyStatement statement, CancellationToken cancellationToken = default)
    {
        _context.OvertimeMonthlyStatements.Add(statement);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
