using Anela.Heblo.Application.Features.Smartsupp.Pipeline;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Smartsupp.Pipeline;

public class DraftReplyLoggingBehaviorTests
{
    private readonly Mock<IRagInteractionLogRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _userService = new();
    private readonly Mock<ILogger<DraftReplyLoggingBehavior>> _logger = new();

    // Prefer a real recorder over a mock: it matches the production flow, where the retrieval
    // and feature handlers populate the scoped recorder that this behavior reads back.
    private readonly RagInteractionRecorder _recorder = new();

    private DraftReplyLoggingBehavior CreateBehavior() =>
        new(_repository.Object, _recorder, _userService.Object, _logger.Object);

    private void RecordInteraction(
        string question = "How do I return an order?",
        int topK = 5,
        string answer = "You can return it within 14 days.",
        string conversationId = "conv-1") =>
        RecordInteraction(question, topK, answer, conversationId, []);

    private void RecordInteraction(
        string question,
        int topK,
        string answer,
        string conversationId,
        IReadOnlyList<RagRetrievedChunk> chunks)
    {
        _recorder.RecordRetrieval(question, topK, chunks);
        _recorder.RecordInteraction(RagFeature.SmartsuppDraftReply, question, "system prompt", answer, conversationId);
    }

    [Fact]
    public async Task Handle_WritesLogRow_AndReturnsResponse()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction(question: "How do I return an order?", topK: 5, answer: "You can return it within 14 days.");

        var request = new GenerateDraftReplyRequest { ConversationId = "conv-1" };
        var expectedResponse = new GenerateDraftReplyResponse
        {
            Answer = "You can return it within 14 days.",
            Sources = []
        };

        RagInteractionLog? capturedLog = null;
        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .Callback<RagInteractionLog, CancellationToken>((log, _) => capturedLog = log)
            .Returns(Task.CompletedTask);

        var behavior = CreateBehavior();
        using var cts = new CancellationTokenSource();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), cts.Token);

        Assert.Equal(expectedResponse, result);
        Assert.NotNull(capturedLog);
        Assert.Equal(RagFeature.SmartsuppDraftReply, capturedLog.Feature);
        Assert.Equal("How do I return an order?", capturedLog.Question);
        Assert.Equal("You can return it within 14 days.", capturedLog.Answer);
        Assert.Equal(5, capturedLog.TopK);
        Assert.Equal(0, capturedLog.SourceCount);
        Assert.Equal("user-1", capturedLog.UserId);
        Assert.True(capturedLog.DurationMs >= 0);
        _repository.Verify(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), cts.Token), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenLogSaved_SetsResponseIdToLogId()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction();

        var request = new GenerateDraftReplyRequest { ConversationId = "conv-1" };
        var expectedResponse = new GenerateDraftReplyResponse { Answer = "answer", Sources = [] };

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
    public async Task Handle_WhenDbWriteFails_StillReturnsResponse()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction();

        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var request = new GenerateDraftReplyRequest { ConversationId = "conv-1" };
        var expectedResponse = new GenerateDraftReplyResponse { Answer = "answer", Sources = [] };

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.Equal("answer", result.Answer);
    }

    [Fact]
    public async Task Handle_WhenDbWriteFails_ResponseIdRemainsNull()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction();

        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var request = new GenerateDraftReplyRequest { ConversationId = "conv-1" };
        var expectedResponse = new GenerateDraftReplyResponse { Answer = "answer", Sources = [] };

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(expectedResponse), default);

        Assert.Null(result.Id);
    }

    [Fact]
    public async Task Handle_WhenNoInteractionRecorded_DoesNotSaveOrSetId()
    {
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        // Deliberately do not record an interaction: recorder.HasInteraction stays false.

        var request = new GenerateDraftReplyRequest { ConversationId = "conv-1" };
        var expectedResponse = new GenerateDraftReplyResponse { Answer = "answer", Sources = [] };

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

        var request = new GenerateDraftReplyRequest { ConversationId = "conv-1" };
        var failedResponse = new GenerateDraftReplyResponse { Answer = "answer", Sources = [], Success = false };

        var behavior = CreateBehavior();
        var result = await behavior.Handle(request, () => Task.FromResult(failedResponse), default);

        Assert.Null(result.Id);
        _repository.Verify(
            r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
