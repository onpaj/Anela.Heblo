using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Pipeline;

public class QuestionLoggingBehaviorTests
{
    private readonly Mock<IRagInteractionLogRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _userService = new();
    private readonly Mock<ILogger<QuestionLoggingBehavior>> _logger = new();

    // Prefer a real recorder over a mock: it matches the production flow, where the retrieval
    // and feature handlers populate the scoped recorder that this behavior reads back.
    private readonly RagInteractionRecorder _recorder = new();

    private QuestionLoggingBehavior CreateBehavior() =>
        new(_repository.Object, _recorder, _userService.Object, _logger.Object);

    private void RecordInteraction(string question = "Test?", int topK = 5, string answer = "Test answer.") =>
        RecordInteraction(question, topK, answer, []);

    private void RecordInteraction(
        string question,
        int topK,
        string answer,
        IReadOnlyList<RagRetrievedChunk> chunks)
    {
        _recorder.RecordRetrieval(question, topK, chunks);
        _recorder.RecordInteraction(RagFeature.KnowledgeBase, question, "system prompt", answer);
    }

    [Fact]
    public async Task Handle_WritesLogRow_AndReturnsResponse()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction(question: "Test?", topK: 5, answer: "Test answer.");

        var request = new AskQuestionRequest { Question = "Test?", TopK = 5 };
        var expectedResponse = new AskQuestionResponse
        {
            Answer = "Test answer.",
            Sources = []
        };

        RagInteractionLog? capturedLog = null;
        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .Callback<RagInteractionLog, CancellationToken>((log, _) => capturedLog = log)
            .Returns(Task.CompletedTask);

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.Equal(expectedResponse, result);
        Assert.NotNull(capturedLog);
        Assert.Equal(RagFeature.KnowledgeBase, capturedLog.Feature);
        Assert.Equal("Test?", capturedLog.Question);
        Assert.Equal("Test answer.", capturedLog.Answer);
        Assert.Equal(5, capturedLog.TopK);
        Assert.Equal(0, capturedLog.SourceCount);
        Assert.Equal("user-1", capturedLog.UserId);
        Assert.True(capturedLog.DurationMs >= 0);
        _repository.Verify(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenDbWriteFails_StillReturnsResponse()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser(null, "Anonymous", null, false));
        RecordInteraction();

        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var request = new AskQuestionRequest { Question = "Test?", TopK = 5 };
        var expectedResponse = new AskQuestionResponse { Answer = "answer", Sources = [] };

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.Equal("answer", result.Answer);
    }

    [Fact]
    public async Task Handle_WhenLogSaved_SetsResponseIdToLogId()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction();

        var request = new AskQuestionRequest { Question = "Test?", TopK = 5 };
        var expectedResponse = new AskQuestionResponse { Answer = "answer", Sources = [] };

        RagInteractionLog? capturedLog = null;
        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .Callback<RagInteractionLog, CancellationToken>((log, _) => capturedLog = log)
            .Returns(Task.CompletedTask);

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.NotNull(capturedLog);
        Assert.Equal(capturedLog.Id, result.Id);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task Handle_WhenDbWriteFails_ResponseIdRemainsNull()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction();

        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var request = new AskQuestionRequest { Question = "Test?", TopK = 5 };
        var expectedResponse = new AskQuestionResponse { Answer = "answer", Sources = [] };

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.Null(result.Id);
    }

    [Fact]
    public async Task Handle_WhenNoInteractionRecorded_DoesNotSaveOrSetId()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        // Deliberately do not record an interaction: recorder.HasInteraction stays false.

        var request = new AskQuestionRequest { Question = "Test?", TopK = 5 };
        var expectedResponse = new AskQuestionResponse { Answer = "answer", Sources = [] };

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.Equal("answer", result.Answer);
        Assert.Null(result.Id);
        _repository.Verify(
            r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenResponseUnsuccessful_DoesNotSaveOrSetId()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction();

        var request = new AskQuestionRequest { Question = "Test?", TopK = 5 };
        var failedResponse = new AskQuestionResponse { Answer = "answer", Sources = [], Success = false };

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(failedResponse), default);

        Assert.Null(result.Id);
        _repository.Verify(
            r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
