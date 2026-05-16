using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;

public class GetTranscriptListRequest : IRequest<GetTranscriptListResponse>
{
    public string? StatusFilter { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
