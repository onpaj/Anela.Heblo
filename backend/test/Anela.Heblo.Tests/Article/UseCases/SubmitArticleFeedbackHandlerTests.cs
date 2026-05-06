using Anela.Heblo.Application.Features.Article.UseCases.SubmitArticleFeedback;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.UseCases;

public class SubmitArticleFeedbackHandlerTests
{
    private readonly Mock<IArticleRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly SubmitArticleFeedbackHandler _handler;

    private const string UserName = "test-user";
    private static readonly Guid ArticleId = Guid.NewGuid();

    public SubmitArticleFeedbackHandlerTests()
    {
        _handler = new SubmitArticleFeedbackHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("user-id", UserName, "test@test.com", true));
    }

    private DomainArticle MakeGeneratedArticle(string? requestedBy = UserName) => new()
    {
        Id = ArticleId,
        Topic = "Test",
        RequestedBy = requestedBy,
        Status = ArticleStatus.Generated,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_WhenArticleNotFound_ReturnsNotFoundError()
    {
        _repositoryMock
            .Setup(x => x.GetByIdForWriteAsync(ArticleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainArticle?)null);

        var result = await _handler.Handle(
            new SubmitArticleFeedbackRequest { ArticleId = ArticleId, PrecisionScore = 3, StyleScore = 4 },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ArticleNotFound);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotOwnArticle_ReturnsForbiddenError()
    {
        var article = MakeGeneratedArticle(requestedBy: "other-user");
        _repositoryMock
            .Setup(x => x.GetByIdForWriteAsync(ArticleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var result = await _handler.Handle(
            new SubmitArticleFeedbackRequest { ArticleId = ArticleId, PrecisionScore = 3, StyleScore = 4 },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenArticleNotGenerated_ReturnsNotGeneratedError()
    {
        var article = MakeGeneratedArticle();
        article.Status = ArticleStatus.Queued;
        _repositoryMock
            .Setup(x => x.GetByIdForWriteAsync(ArticleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var result = await _handler.Handle(
            new SubmitArticleFeedbackRequest { ArticleId = ArticleId, PrecisionScore = 3, StyleScore = 4 },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ArticleNotGenerated);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPrecisionScoreAlreadySet_ReturnsConflictError()
    {
        var article = MakeGeneratedArticle();
        article.PrecisionScore = 4;
        _repositoryMock
            .Setup(x => x.GetByIdForWriteAsync(ArticleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var result = await _handler.Handle(
            new SubmitArticleFeedbackRequest { ArticleId = ArticleId, PrecisionScore = 3, StyleScore = 4 },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ArticleFeedbackAlreadySubmitted);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenStyleScoreAlreadySet_ReturnsConflictError()
    {
        var article = MakeGeneratedArticle();
        article.StyleScore = 2;
        _repositoryMock
            .Setup(x => x.GetByIdForWriteAsync(ArticleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var result = await _handler.Handle(
            new SubmitArticleFeedbackRequest { ArticleId = ArticleId, PrecisionScore = 3, StyleScore = 4 },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ArticleFeedbackAlreadySubmitted);
    }

    [Fact]
    public async Task Handle_ValidRequest_SavesFeedbackAndReturnsSuccess()
    {
        var article = MakeGeneratedArticle();
        _repositoryMock
            .Setup(x => x.GetByIdForWriteAsync(ArticleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);
        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(
            new SubmitArticleFeedbackRequest
            {
                ArticleId = ArticleId,
                PrecisionScore = 4,
                StyleScore = 3,
                Comment = "Dobrý článek"
            },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        article.PrecisionScore.Should().Be(4);
        article.StyleScore.Should().Be(3);
        article.FeedbackComment.Should().Be("Dobrý článek");
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequestNoComment_SavesNullComment()
    {
        var article = MakeGeneratedArticle();
        _repositoryMock
            .Setup(x => x.GetByIdForWriteAsync(ArticleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);
        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _handler.Handle(
            new SubmitArticleFeedbackRequest { ArticleId = ArticleId, PrecisionScore = 5, StyleScore = 5 },
            CancellationToken.None);

        article.FeedbackComment.Should().BeNull();
    }
}
