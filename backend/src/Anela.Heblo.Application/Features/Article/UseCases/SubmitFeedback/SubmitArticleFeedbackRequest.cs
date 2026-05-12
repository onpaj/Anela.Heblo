using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.SubmitFeedback;

public class SubmitArticleFeedbackRequest : IRequest<SubmitArticleFeedbackResponse>
{
    public Guid ArticleId { get; set; }

    [Range(1, 5)]
    public int PrecisionScore { get; set; }

    [Range(1, 5)]
    public int StyleScore { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }
}

public class SubmitArticleFeedbackResponse : BaseResponse
{
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public string? FeedbackComment { get; set; }

    public SubmitArticleFeedbackResponse()
    {
    }

    public SubmitArticleFeedbackResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters)
    {
    }
}
