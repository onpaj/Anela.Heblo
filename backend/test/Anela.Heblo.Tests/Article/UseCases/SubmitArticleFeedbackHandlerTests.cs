using Anela.Heblo.Application.Features.Article.UseCases.SubmitFeedback;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.UseCases;

public class SubmitArticleFeedbackHandlerTests
{
    private readonly Mock<IArticleRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private SubmitArticleFeedbackHandler CreateHandler() =>
        new(_repository.Object, _currentUser.Object);

    private static DomainArticle CreateArticle(
        string requestedBy = "alice",
        ArticleStatus status = ArticleStatus.Generated,
        int? precisionScore = null,
        int? styleScore = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Topic = "Topic",
            RequestedBy = requestedBy,
            Status = status,
            PrecisionScore = precisionScore,
            StyleScore = styleScore,
        };

    private void SetCurrentUser(string name) =>
        _currentUser.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "id-of-" + name, Name: name, Email: null, IsAuthenticated: true));

    [Fact]
    public async Task Handle_ArticleMissing_ReturnsArticleNotFound()
    {
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = Guid.NewGuid(),
            PrecisionScore = 4,
            StyleScore = 5,
        };
        SetCurrentUser("alice");
        _repository.Setup(r => r.GetForUpdateAsync(request.ArticleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainArticle?)null);

        var response = await CreateHandler().Handle(request, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ArticleNotFound);
    }

    [Fact]
    public async Task Handle_OtherUser_ReturnsForbidden()
    {
        var article = CreateArticle(requestedBy: "alice");
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = article.Id,
            PrecisionScore = 4,
            StyleScore = 5,
        };
        SetCurrentUser("bob");
        _repository.Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(request, default);

        response.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ArticleNotGenerated_ReturnsArticleNotGenerated()
    {
        var article = CreateArticle(status: ArticleStatus.Writing);
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = article.Id,
            PrecisionScore = 3,
            StyleScore = 3,
        };
        SetCurrentUser("alice");
        _repository.Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(request, default);

        response.ErrorCode.Should().Be(ErrorCodes.ArticleNotGenerated);
    }

    [Fact]
    public async Task Handle_AlreadySubmitted_ReturnsAlreadySubmittedConflict()
    {
        var article = CreateArticle(precisionScore: 4);
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = article.Id,
            PrecisionScore = 5,
            StyleScore = 5,
        };
        SetCurrentUser("alice");
        _repository.Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(request, default);

        response.ErrorCode.Should().Be(ErrorCodes.ArticleFeedbackAlreadySubmitted);
    }

    [Fact]
    public async Task Handle_HappyPath_PersistsFeedbackAndReturnsValues()
    {
        var article = CreateArticle();
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = article.Id,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great",
        };
        SetCurrentUser("alice");
        _repository.Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(request, default);

        response.Success.Should().BeTrue();
        response.PrecisionScore.Should().Be(5);
        response.StyleScore.Should().Be(4);
        response.FeedbackComment.Should().Be("Great");
        article.PrecisionScore.Should().Be(5);
        article.StyleScore.Should().Be(4);
        article.FeedbackComment.Should().Be("Great");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
