using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.SubmitFeedback;

public sealed class SubmitArticleFeedbackHandler
    : IRequestHandler<SubmitArticleFeedbackRequest, SubmitArticleFeedbackResponse>
{
    private readonly IArticleRepository _repository;
    private readonly ICurrentUserService _currentUser;

    public SubmitArticleFeedbackHandler(
        IArticleRepository repository,
        ICurrentUserService currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<SubmitArticleFeedbackResponse> Handle(
        SubmitArticleFeedbackRequest request,
        CancellationToken ct)
    {
        var article = await _repository.GetForUpdateAsync(request.ArticleId, ct);
        if (article is null)
        {
            return new SubmitArticleFeedbackResponse(
                ErrorCodes.ArticleNotFound,
                new Dictionary<string, string> { { "id", request.ArticleId.ToString() } });
        }

        var user = _currentUser.GetCurrentUser();
        if (article.RequestedBy is null ||
            !string.Equals(article.RequestedBy, user.GetIdentifier(), StringComparison.Ordinal))
        {
            return new SubmitArticleFeedbackResponse(
                ErrorCodes.Forbidden,
                new Dictionary<string, string> { { "id", request.ArticleId.ToString() } });
        }

        if (article.Status != ArticleStatus.Generated)
        {
            return new SubmitArticleFeedbackResponse(
                ErrorCodes.ArticleNotGenerated,
                new Dictionary<string, string> { { "id", request.ArticleId.ToString() } });
        }

        if (article.PrecisionScore is not null || article.StyleScore is not null)
        {
            return new SubmitArticleFeedbackResponse(
                ErrorCodes.ArticleFeedbackAlreadySubmitted,
                new Dictionary<string, string> { { "id", request.ArticleId.ToString() } });
        }

        article.SubmitFeedback(request.PrecisionScore, request.StyleScore, request.Comment);

        await _repository.SaveChangesAsync(ct);

        return new SubmitArticleFeedbackResponse
        {
            PrecisionScore = article.PrecisionScore,
            StyleScore = article.StyleScore,
            FeedbackComment = article.FeedbackComment,
        };
    }
}
