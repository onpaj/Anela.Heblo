using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public class MeetingAccessGuard : IMeetingAccessGuard
{
    private readonly ICurrentUserService _currentUserService;

    public MeetingAccessGuard(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    public bool IsManager() => _currentUserService.IsInRole(AuthorizationConstants.Roles.MeetingManager);

    public bool CanAccess(MeetingTranscript transcript)
    {
        if (IsManager()) return true;

        var email = _currentUserService.GetCurrentUser().Email;
        if (string.IsNullOrWhiteSpace(email)) return false;

        return transcript.AccessLevel switch
        {
            MeetingAccessLevel.Public => true,
            MeetingAccessLevel.Private => false,
            MeetingAccessLevel.Restricted => transcript.AccessGrants.Any(
                g => g.UserEmail.Equals(email, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }
}
