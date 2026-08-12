using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.AttachMeeting;

public class AttachMeetingRequest : IRequest<AttachMeetingResponse>
{
    public Guid MindMapId { get; set; }

    [Required]
    public Guid MeetingTranscriptId { get; set; }
}
