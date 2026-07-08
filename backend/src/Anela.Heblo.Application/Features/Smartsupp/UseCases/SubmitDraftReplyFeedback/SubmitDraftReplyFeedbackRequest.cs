using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SubmitDraftReplyFeedback;

public class SubmitDraftReplyFeedbackRequest : IRequest<SubmitDraftReplyFeedbackResponse>
{
    public Guid LogId { get; set; }

    [Range(1, 5)]
    public int PrecisionScore { get; set; }

    [Range(1, 5)]
    public int StyleScore { get; set; }

    public string? Comment { get; set; }
}

public class SubmitDraftReplyFeedbackResponse : BaseResponse
{
    public SubmitDraftReplyFeedbackResponse()
    {
    }

    public SubmitDraftReplyFeedbackResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null)
        : base(errorCode, details)
    {
    }
}
