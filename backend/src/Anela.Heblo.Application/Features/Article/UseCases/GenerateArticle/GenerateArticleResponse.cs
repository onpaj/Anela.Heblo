using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.GenerateArticle;

public sealed class GenerateArticleResponse : BaseResponse
{
    public Guid? ArticleId { get; set; }

    public string? HangfireJobId { get; set; }

    /// <summary>
    /// Initial article status. Meaningful only when <see cref="BaseResponse.Success"/> is true;
    /// on failure responses defaults to <see cref="ArticleStatus.Queued"/> (enum zero) and must be ignored.
    /// </summary>
    public ArticleStatus Status { get; set; }

    public GenerateArticleResponse() { }

    public GenerateArticleResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
