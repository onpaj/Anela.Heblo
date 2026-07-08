using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SubmitFeedback;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.KnowledgeBase;

public class SubmitFeedbackHandlerTests
{
    private readonly Mock<IRagInteractionLogRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly SubmitFeedbackHandler _handler;

    private const string UserId = "user-123";

    public SubmitFeedbackHandlerTests()
    {
        _repositoryMock = new Mock<IRagInteractionLogRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _handler = new SubmitFeedbackHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: UserId, Name: "Test User", Email: "test@example.com", IsAuthenticated: true));
    }

    [Fact]
    public async Task Handle_WhenLogNotFound_ShouldReturnNotFoundError()
    {
        var logId = Guid.NewGuid();
        _repositoryMock
            .Setup(x => x.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RagInteractionLog?)null);

        var request = new SubmitFeedbackRequest { LogId = logId, PrecisionScore = 8, StyleScore = 7 };
        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.KnowledgeBaseFeedbackLogNotFound);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotOwnLog_ShouldReturnForbiddenError()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.KnowledgeBase,
            Question = "test",
            Answer = "answer",
            UserId = "other-user"
        };

        _repositoryMock
            .Setup(x => x.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);

        var request = new SubmitFeedbackRequest { LogId = logId, PrecisionScore = 8, StyleScore = 7 };
        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenFeedbackAlreadySubmitted_ShouldReturnConflictError()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.KnowledgeBase,
            Question = "test",
            Answer = "answer",
            UserId = UserId,
            PrecisionScore = 9
        };

        _repositoryMock
            .Setup(x => x.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);

        var request = new SubmitFeedbackRequest { LogId = logId, PrecisionScore = 8, StyleScore = 7 };
        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.KnowledgeBaseFeedbackAlreadySubmitted);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenStyleScoreAlreadySet_ShouldReturnConflictError()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.KnowledgeBase,
            Question = "test",
            Answer = "answer",
            UserId = UserId,
            StyleScore = 5
        };

        _repositoryMock
            .Setup(x => x.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);

        var request = new SubmitFeedbackRequest { LogId = logId, PrecisionScore = 8, StyleScore = 7 };
        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.KnowledgeBaseFeedbackAlreadySubmitted);
    }

    [Fact]
    public async Task Handle_WhenValidRequest_ShouldSaveFeedbackAndReturnSuccess()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.KnowledgeBase,
            Question = "test",
            Answer = "answer",
            UserId = UserId
        };

        _repositoryMock
            .Setup(x => x.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new SubmitFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 8,
            StyleScore = 7,
            Comment = "Great answer"
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        log.PrecisionScore.Should().Be(8);
        log.StyleScore.Should().Be(7);
        log.FeedbackComment.Should().Be("Great answer");
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenValidRequestWithNoComment_ShouldSaveFeedbackWithNullComment()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.KnowledgeBase,
            Question = "test",
            Answer = "answer",
            UserId = UserId
        };

        _repositoryMock
            .Setup(x => x.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new SubmitFeedbackRequest { LogId = logId, PrecisionScore = 5, StyleScore = 6 };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        log.PrecisionScore.Should().Be(5);
        log.StyleScore.Should().Be(6);
        log.FeedbackComment.Should().BeNull();
    }
}
