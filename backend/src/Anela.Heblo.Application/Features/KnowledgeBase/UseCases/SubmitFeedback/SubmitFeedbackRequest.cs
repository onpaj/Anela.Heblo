using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SubmitFeedback;

public class SubmitFeedbackRequest : IRequest<SubmitFeedbackResponse>
{
    public Guid LogId { get; set; }

    [Range(1, 10)]
    public int PrecisionScore { get; set; }

    [Range(1, 10)]
    public int StyleScore { get; set; }

    public string? Comment { get; set; }
}

public class SubmitFeedbackResponse : BaseResponse
{
    public SubmitFeedbackResponse()
    {
    }

    public SubmitFeedbackResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null)
        : base(errorCode, details)
    {
    }
}
