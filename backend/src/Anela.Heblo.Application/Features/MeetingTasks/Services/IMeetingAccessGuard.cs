using Anela.Heblo.Domain.Features.MeetingTasks;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public interface IMeetingAccessGuard
{
    bool IsManager();
    bool CanAccess(MeetingTranscript transcript);
}
