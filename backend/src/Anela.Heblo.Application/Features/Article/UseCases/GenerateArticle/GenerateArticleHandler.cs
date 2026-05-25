using Anela.Heblo.Application.Features.Article.UseCases.Generate;
using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Domain.Features.Users;
using Hangfire;
using MediatR;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.GenerateArticle;

public sealed class GenerateArticleHandler : IRequestHandler<GenerateArticleRequest, GenerateArticleResponse>
{
    private readonly IArticleRepository _repository;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ICurrentUserService _currentUserService;

    public GenerateArticleHandler(
        IArticleRepository repository,
        IBackgroundJobClient backgroundJobClient,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _backgroundJobClient = backgroundJobClient;
        _currentUserService = currentUserService;
    }

    public async Task<GenerateArticleResponse> Handle(
        GenerateArticleRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();

        var article = new DomainArticle
        {
            Id = Guid.NewGuid(),
            Topic = request.Topic,
            Scope = request.Scope,
            Audience = request.Audience,
            Angle = request.Angle,
            Length = request.Length,
            LanguageNote = request.LanguageNote,
            UsedKnowledgeBase = request.UseKnowledgeBase,
            UsedWebSearch = request.UseWebSearch,
            StyleGuideDriveId = request.StyleGuideDriveId,
            StyleGuideItemPath = request.StyleGuideItemPath,
            Status = ArticleStatus.Queued,
            RequestedBy = currentUser.IsAuthenticated ? currentUser.Name : null,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.AddAsync(article, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var jobId = _backgroundJobClient.Enqueue<GenerateArticleJob>(
            j => j.RunAsync(article.Id, CancellationToken.None));

        return new GenerateArticleResponse
        {
            ArticleId = article.Id,
            HangfireJobId = jobId,
            Status = ArticleStatus.Queued,
        };
    }
}
