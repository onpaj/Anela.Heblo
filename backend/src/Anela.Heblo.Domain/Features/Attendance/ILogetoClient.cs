namespace Anela.Heblo.Domain.Features.Attendance;

public interface ILogetoClient
{
    Task<IReadOnlyList<LogetoActivity>> GetActivitiesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LogetoPerson>> GetPeopleAsync(CancellationToken cancellationToken);

    /// <summary>Returns all records in the date range for all people (the API has no person filter).</summary>
    Task<IReadOnlyList<LogetoTimeEntry>> GetTimeTrackingAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Creates a time entry. With merge=true, overlapping records are merged/split by Logeto.</summary>
    Task CreateTimeEntryAsync(LogetoTimeEntryRequest request, bool merge, CancellationToken cancellationToken);

    /// <summary>Replaces an existing time entry wholesale (merge=false); every preserved field
    /// must be present in the request.</summary>
    Task UpdateTimeEntryAsync(Guid guid, LogetoTimeEntryRequest request, CancellationToken cancellationToken);
}
