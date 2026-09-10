using Anela.Heblo.Application.Features.Smartsupp.UseCases.SubmitDraftReplyFeedback;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SubmitDraftReplyFeedbackHandlerTests
{
    private readonly Mock<IRagInteractionLogRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    private SubmitDraftReplyFeedbackHandler CreateHandler() =>
        new(_repository.Object, _currentUserService.Object);

    [Fact]
    public async Task Handle_LogNotFound_ReturnsNotFound()
    {
        var logId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RagInteractionLog?)null);

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great answer",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound);
        result.Params.Should().ContainKey("logId").WhoseValue.Should().Be(logId.ToString());
        _currentUserService.Verify(s => s.GetCurrentUser(), Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WrongFeature_ReturnsNotFound()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.KnowledgeBase,
            PrecisionScore = null,
            StyleScore = null,
        };
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great answer",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OwnershipMismatch_ReturnsForbidden()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.SmartsuppDraftReply,
            UserId = "user-a",
            PrecisionScore = null,
            StyleScore = null,
        };
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-b", "User B", "user-b@example.com", true));

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great answer",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Params.Should().ContainKey("logId").WhoseValue.Should().Be(logId.ToString());
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PrecisionScoreAlreadySet_ReturnsAlreadySubmitted()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.SmartsuppDraftReply,
            UserId = "user-a",
            PrecisionScore = 3,
            StyleScore = null,
            FeedbackComment = null,
        };
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-a", "User A", "user-a@example.com", true));

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great answer",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted);
        log.PrecisionScore.Should().Be(3);
        log.StyleScore.Should().BeNull();
        log.FeedbackComment.Should().BeNull();
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_StyleScoreAlreadySet_ReturnsAlreadySubmitted()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.SmartsuppDraftReply,
            UserId = "user-a",
            PrecisionScore = null,
            StyleScore = 4,
        };
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-a", "User A", "user-a@example.com", true));

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great answer",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Success_WritesScoresAndSaves()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.SmartsuppDraftReply,
            UserId = "user-a",
            PrecisionScore = null,
            StyleScore = null,
            FeedbackComment = null,
        };
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-a", "User A", "user-a@example.com", true));

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great answer",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        log.PrecisionScore.Should().Be(5);
        log.StyleScore.Should().Be(4);
        log.FeedbackComment.Should().Be("Great answer");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Success_NullComment_WritesNull()
    {
        var logId = Guid.NewGuid();
        var log = new RagInteractionLog
        {
            Id = logId,
            Feature = RagFeature.SmartsuppDraftReply,
            UserId = "user-a",
            PrecisionScore = null,
            StyleScore = null,
            FeedbackComment = null,
        };
        _repository
            .Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-a", "User A", "user-a@example.com", true));

        var request = new SubmitDraftReplyFeedbackRequest
        {
            LogId = logId,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = null,
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        log.FeedbackComment.Should().BeNull();
    }
}
