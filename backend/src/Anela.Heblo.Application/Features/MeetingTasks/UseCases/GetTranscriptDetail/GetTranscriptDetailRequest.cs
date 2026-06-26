using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;

public class GetTranscriptDetailRequest : IRequest<GetTranscriptDetailResponse>
{
    public Guid Id { get; set; }
}
