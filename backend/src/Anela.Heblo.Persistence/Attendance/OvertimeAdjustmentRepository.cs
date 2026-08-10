using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Attendance;

public class OvertimeAdjustmentRepository : IOvertimeAdjustmentRepository
{
    private readonly ApplicationDbContext _context;

    public OvertimeAdjustmentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<OvertimeAdjustment>> GetByMonthAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeAdjustments
            .Where(a => a.Year == year && a.Month == month)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OvertimeAdjustment>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeAdjustments
            .OrderBy(a => a.Year).ThenBy(a => a.Month).ThenBy(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<OvertimeAdjustment?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeAdjustments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task AddAsync(OvertimeAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        _context.OvertimeAdjustments.Add(adjustment);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(OvertimeAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        _context.OvertimeAdjustments.Remove(adjustment);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
