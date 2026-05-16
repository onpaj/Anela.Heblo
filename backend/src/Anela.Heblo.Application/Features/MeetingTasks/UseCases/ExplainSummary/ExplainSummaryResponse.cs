using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ExplainSummary;

public class ExplainSummaryResponse : BaseResponse
{
    public ExplainSummaryResponse() { }

    public ExplainSummaryResponse(ErrorCodes errorCode) : base(errorCode) { }

    public string RelevantTranscript { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}
