using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Pipeline;

public class QuestionLoggingBehaviorTests
{
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _userService = new();
    private readonly Mock<ILogger<QuestionLoggingBehavior>> _logger = new();

    private QuestionLoggingBehavior CreateBehavior() =>
        new(_repository.Object, _userService.Object, _logger.Object);

    [Fact]
    public async Task Handle_WritesLogRow_AndReturnsResponse()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));

        var request = new AskQuestionRequest { Question = "Test?", TopK = 5 };
        var expectedResponse = new AskQuestionResponse
        {
            Answer = "Test answer.",
            Sources = []
        };

        KnowledgeBaseQuestionLog? capturedLog = null;
        _repository
            .Setup(r => r.SaveQuestionLogAsync(It.IsAny<KnowledgeBaseQuestionLog>(), It.IsAny<CancellationToken>()))
            .Callback<KnowledgeBaseQuestionLog, CancellationToken>((log, _) => capturedLog = log)
            .Returns(Task.CompletedTask);

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.Equal(expectedResponse, result);
        Assert.NotNull(capturedLog);
        Assert.Equal("Test?", capturedLog.Question);
        Assert.Equal("Test answer.", capturedLog.Answer);
        Assert.Equal(5, capturedLog.TopK);
        Assert.Equal(0, capturedLog.SourceCount);
        Assert.Equal("user-1", capturedLog.UserId);
        Assert.True(capturedLog.DurationMs >= 0);
        _repository.Verify(r => r.SaveQuestionLogAsync(It.IsAny<KnowledgeBaseQuestionLog>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenDbWriteFails_StillReturnsResponse()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser(null, "Anonymous", null, false));

        _repository
            .Setup(r => r.SaveQuestionLogAsync(It.IsAny<KnowledgeBaseQuestionLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var request = new AskQuestionRequest { Question = "Test?", TopK = 5 };
        var expectedResponse = new AskQuestionResponse { Answer = "answer", Sources = [] };

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.Equal("answer", result.Answer);
    }
}
