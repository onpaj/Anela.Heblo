using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateTranscriptStatus;

public class UpdateTranscriptStatusResponse : BaseResponse
{
    public string? Status { get; set; }

    public UpdateTranscriptStatusResponse() { }
    public UpdateTranscriptStatusResponse(ErrorCodes errorCode) : base(errorCode) { }
    public UpdateTranscriptStatusResponse(ErrorCodes errorCode, Dictionary<string, string> parameters)
        : base(errorCode, parameters) { }
}
