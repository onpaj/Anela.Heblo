using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;

public class GetTranscriptListRequest : IRequest<GetTranscriptListResponse>
{
    public string? StatusFilter { get; set; }
    public string? SearchText { get; set; }
    public bool SearchInTranscript { get; set; } = false;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
