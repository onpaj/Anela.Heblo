using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateMeetingAccess;

public class UpdateMeetingAccessRequest : IRequest<UpdateMeetingAccessResponse>
{
    public Guid TranscriptId { get; set; }
    public string AccessLevel { get; set; } = null!;
    public List<string> RestrictedUserEmails { get; set; } = new();
}
