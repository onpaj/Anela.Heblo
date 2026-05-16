namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public interface IMeetingUserDirectory
{
    /// <summary>All known users from the static directory file.</summary>
    IReadOnlyList<MeetingUser> GetAll();

    /// <summary>
    /// Resolve a free-form name or alias (case-insensitive) to a known user.
    /// Returns null when no display name or alias matches.
    /// </summary>
    MeetingUser? Resolve(string nameOrAlias);
}
