using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;

public class GetTranscriptDetailResponse : BaseResponse
{
    public GetTranscriptDetailResponse() { }
    public GetTranscriptDetailResponse(ErrorCodes errorCode) : base(errorCode) { }

    public MeetingTranscriptDto Transcript { get; set; } = null!;
}
