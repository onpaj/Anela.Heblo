namespace Anela.Heblo.Domain.Features.Attendance;

public interface ILogetoClient
{
    Task<IReadOnlyList<LogetoActivity>> GetActivitiesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LogetoPerson>> GetPeopleAsync(CancellationToken cancellationToken);

    /// <summary>Returns all records in the date range for all people (the API has no person filter).</summary>
    Task<IReadOnlyList<LogetoTimeEntry>> GetTimeTrackingAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Creates a time entry. With merge=true, overlapping records are merged/split by Logeto.</summary>
    Task CreateTimeEntryAsync(LogetoCreateTimeEntryRequest request, bool merge, CancellationToken cancellationToken);
}
