using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetQuestionTagStats;

public class GetQuestionTagStatsRequest : IRequest<GetQuestionTagStatsResponse>
{
    /// <summary>Inclusive first day (Europe/Prague). Null = no lower bound.</summary>
    public DateOnly? DateFrom { get; set; }

    /// <summary>Inclusive last day (Europe/Prague). Null = no upper bound.</summary>
    public DateOnly? DateTo { get; set; }
}

public class QuestionTagCountDto
{
    /// <summary>The tag value used for filtering (a normalised tag, or a product code for recommended products).</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Display text: the tag itself, or the catalog product name for recommended products.</summary>
    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }
}

public class GetQuestionTagStatsResponse : BaseResponse
{
    public List<QuestionTagCountDto> ProblemTags { get; set; } = [];
    public List<QuestionTagCountDto> ProductTags { get; set; } = [];
    public List<QuestionTagCountDto> RecommendedProducts { get; set; } = [];
}
