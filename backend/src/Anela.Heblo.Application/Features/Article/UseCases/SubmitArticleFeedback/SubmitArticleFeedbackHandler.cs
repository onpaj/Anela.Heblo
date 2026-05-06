using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.SubmitArticleFeedback;

public class SubmitArticleFeedbackHandler : IRequestHandler<SubmitArticleFeedbackRequest, SubmitArticleFeedbackResponse>
{
    private readonly IArticleRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SubmitArticleFeedbackHandler(
        IArticleRepository repository,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<SubmitArticleFeedbackResponse> Handle(
        SubmitArticleFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var article = await _repository.GetByIdForWriteAsync(request.ArticleId, cancellationToken);
        if (article is null)
            return new SubmitArticleFeedbackResponse(ErrorCodes.ArticleNotFound,
                new() { { "articleId", request.ArticleId.ToString() } });

        var currentUser = _currentUserService.GetCurrentUser();
        if (article.RequestedBy != currentUser.Name)
            return new SubmitArticleFeedbackResponse(ErrorCodes.Forbidden,
                new() { { "articleId", request.ArticleId.ToString() } });

        if (article.Status != ArticleStatus.Generated)
            return new SubmitArticleFeedbackResponse(ErrorCodes.ArticleNotGenerated,
                new() { { "status", article.Status.ToString() } });

        if (article.PrecisionScore is not null || article.StyleScore is not null)
            return new SubmitArticleFeedbackResponse(ErrorCodes.ArticleFeedbackAlreadySubmitted,
                new() { { "articleId", request.ArticleId.ToString() } });

        article.PrecisionScore = request.PrecisionScore;
        article.StyleScore = request.StyleScore;
        article.FeedbackComment = request.Comment;

        await _repository.SaveChangesAsync(cancellationToken);

        return new SubmitArticleFeedbackResponse();
    }
}
