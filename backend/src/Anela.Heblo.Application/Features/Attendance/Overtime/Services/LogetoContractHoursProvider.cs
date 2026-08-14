using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

/// <summary>
/// Daily contract hours come from the person's Logeto Note ("integration 6,4") — the single
/// source of truth for úvazek, since the Logeto API exposes it nowhere else. See
/// docs/superpowers/specs/2026-08-14-logeto-contract-hours-single-source.md.
///
/// Registered scoped: the people lookup is memoized for the lifetime of the scope, so one
/// request or job run issues a single Logeto call however many people it asks about.
/// </summary>
public class LogetoContractHoursProvider : IContractHoursProvider
{
    private readonly ILogetoClient _client;
    private Task<IReadOnlyDictionary<Guid, TimeSpan?>>? _hoursByPerson;

    public LogetoContractHoursProvider(ILogetoClient client)
    {
        _client = client;
    }

    /// <summary>
    /// year/month are ignored: a Note states only the person's current úvazek and carries no
    /// history. Closed statements freeze their own RequiredHours, so only open months
    /// recompute — and an open month should follow the current note.
    /// </summary>
    public async Task<decimal?> GetDailyHoursAsync(
        Guid personId, int year, int month, CancellationToken cancellationToken)
    {
        var hoursByPerson = await LoadOnceAsync(cancellationToken);

        return hoursByPerson.TryGetValue(personId, out var hours) && hours.HasValue
            ? (decimal)hours.Value.Ticks / TimeSpan.TicksPerHour
            : null;
    }

    private Task<IReadOnlyDictionary<Guid, TimeSpan?>> LoadOnceAsync(CancellationToken cancellationToken) =>
        // Memoizes the task rather than the result, so concurrent callers in one scope share
        // a single request instead of racing. A failure is cached for the scope too: one
        // outage surfaces once, not once per person.
        _hoursByPerson ??= LoadAsync(cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, TimeSpan?>> LoadAsync(CancellationToken cancellationToken)
    {
        var people = await _client.GetPeopleAsync(cancellationToken);
        var hoursByPerson = new Dictionary<Guid, TimeSpan?>();

        foreach (var person in people)
        {
            var note = IntegrationNote.Parse(person.Note);
            if (note.IsEnrolled)
            {
                hoursByPerson[person.Guid] = note.DailyHours;
            }
        }

        return hoursByPerson;
    }
}
