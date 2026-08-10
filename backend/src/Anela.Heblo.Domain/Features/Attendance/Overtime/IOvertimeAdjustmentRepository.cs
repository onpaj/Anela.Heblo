namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

public interface IOvertimeAdjustmentRepository
{
    Task<IReadOnlyList<OvertimeAdjustment>> GetByMonthAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<OvertimeAdjustment?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task AddAsync(OvertimeAdjustment adjustment, CancellationToken cancellationToken = default);
    Task DeleteAsync(OvertimeAdjustment adjustment, CancellationToken cancellationToken = default);
}
