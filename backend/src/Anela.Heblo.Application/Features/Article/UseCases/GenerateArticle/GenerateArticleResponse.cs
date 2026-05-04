using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Article.UseCases.GenerateArticle;

public class GenerateArticleResponse : BaseResponse
{
    public Guid? ArticleId { get; set; }

    public GenerateArticleResponse() { }

    public GenerateArticleResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
