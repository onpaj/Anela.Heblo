namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

public interface IOvertimeEmployeeRepository
{
    Task<IReadOnlyList<OvertimeEmployee>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<OvertimeEmployee?> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default);
    Task UpsertAsync(OvertimeEmployee employee, CancellationToken cancellationToken = default);
}
