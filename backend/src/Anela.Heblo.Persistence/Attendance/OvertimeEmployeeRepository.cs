using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Attendance;

public class OvertimeEmployeeRepository : IOvertimeEmployeeRepository
{
    private readonly ApplicationDbContext _context;

    public OvertimeEmployeeRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<OvertimeEmployee>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeEmployees.OrderBy(e => e.DisplayName).ToListAsync(cancellationToken);
    }

    public async Task<OvertimeEmployee?> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeEmployees.FirstOrDefaultAsync(e => e.PersonId == personId, cancellationToken);
    }

    public async Task UpsertAsync(OvertimeEmployee employee, CancellationToken cancellationToken = default)
    {
        var existing = await _context.OvertimeEmployees
            .FirstOrDefaultAsync(e => e.PersonId == employee.PersonId, cancellationToken);

        if (existing is null)
        {
            _context.OvertimeEmployees.Add(employee);
        }
        else
        {
            existing.DisplayName = employee.DisplayName;
            existing.BaselineHours = employee.BaselineHours;
            existing.BaselineDate = employee.BaselineDate;
            existing.IsActive = employee.IsActive;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
