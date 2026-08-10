namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

public interface IOvertimeStatementRepository
{
    Task<IReadOnlyList<OvertimeMonthlyStatement>> GetByMonthAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<OvertimeMonthlyStatement?> GetLatestClosedAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OvertimeMonthlyStatement>> GetAllClosedAsync(CancellationToken cancellationToken = default);
    Task<bool> AnyOpenBeforeAsync(int year, int month, CancellationToken cancellationToken = default);
    Task AddAsync(OvertimeMonthlyStatement statement, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
