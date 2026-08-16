namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

/// <summary>Daily contract hours (úvazek, without break — e.g. 6.4) per person and month.
/// Currently configuration-backed; will be swapped for a Logeto-backed implementation
/// once the Logeto úvazek API investigation lands.</summary>
public interface IContractHoursProvider
{
    Task<decimal?> GetDailyHoursAsync(Guid personId, int year, int month, CancellationToken cancellationToken);
}
