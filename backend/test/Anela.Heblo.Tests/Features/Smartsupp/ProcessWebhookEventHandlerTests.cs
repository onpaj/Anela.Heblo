using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class ProcessWebhookEventHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppWebhookMetrics> _metrics = new();

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static ProcessWebhookEventRequest MakeRequest(string eventName, string dataJson = "{}") =>
        new()
        {
            EventName = eventName,
            Timestamp = DateTime.UtcNow,
            AccountId = "acc-1",
            AppId = "app-1",
            Data = Parse(dataJson),
        };

    private ProcessWebhookEventHandler CreateHandler(
        IEnumerable<ISmartsuppWebhookReaction>? reactions = null)
    {
        reactions ??= Array.Empty<ISmartsuppWebhookReaction>();
        return new ProcessWebhookEventHandler(
            reactions,
            _repo.Object,
            _metrics.Object,
            NullLogger<ProcessWebhookEventHandler>.Instance);
    }

    [Fact]
    public async Task Handle_KnownEvent_InvokesMatchingReaction_AndSavesChanges()
    {
        var reaction = new Mock<ISmartsuppWebhookReaction>();
        reaction.Setup(r => r.EventName).Returns("conversation.opened");

        var handler = CreateHandler(new[] { reaction.Object });
        var request = MakeRequest("conversation.opened");

        var response = await handler.Handle(request, CancellationToken.None);

        response.Handled.Should().BeTrue();
        reaction.Verify(r => r.HandleAsync(It.IsAny<WebhookEventContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _metrics.Verify(m => m.RecordReceived("conversation.opened", "handled", It.IsAny<double>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownEvent_ReturnsHandledFalse_WithReasonUnknown()
    {
        var handler = CreateHandler();
        var response = await handler.Handle(MakeRequest("conversation.something_new"), CancellationToken.None);

        response.Handled.Should().BeFalse();
        response.Reason.Should().Be("unknown");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _metrics.Verify(m => m.RecordReceived("conversation.something_new", "unknown", It.IsAny<double>()), Times.Once);
    }

    [Fact]
    public async Task Handle_VisitorEvent_ReturnsHandledFalse_WithReasonObserved()
    {
        var handler = CreateHandler();
        var response = await handler.Handle(MakeRequest("visitor.connected"), CancellationToken.None);

        response.Handled.Should().BeFalse();
        response.Reason.Should().Be("observed");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _metrics.Verify(m => m.RecordReceived("visitor.connected", "observed", It.IsAny<double>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AppEvent_ReturnsHandledFalse_WithReasonIgnored()
    {
        var handler = CreateHandler();
        var response = await handler.Handle(MakeRequest("app.installed"), CancellationToken.None);

        response.Handled.Should().BeFalse();
        response.Reason.Should().Be("ignored");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _metrics.Verify(m => m.RecordReceived("app.installed", "ignored", It.IsAny<double>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReactionThrows_RecordsErrorMetric_AndRethrows()
    {
        var reaction = new Mock<ISmartsuppWebhookReaction>();
        reaction.Setup(r => r.EventName).Returns("conversation.opened");
        reaction.Setup(r => r.HandleAsync(It.IsAny<WebhookEventContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("test error"));

        var handler = CreateHandler(new[] { reaction.Object });

        var act = async () => await handler.Handle(MakeRequest("conversation.opened"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _metrics.Verify(m => m.RecordReceived("conversation.opened", "error", It.IsAny<double>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SaveChangesCalledOnce_AfterReactionSucceeds()
    {
        var reaction = new Mock<ISmartsuppWebhookReaction>();
        reaction.Setup(r => r.EventName).Returns("contact.created");

        var handler = CreateHandler(new[] { reaction.Object });
        await handler.Handle(MakeRequest("contact.created"), CancellationToken.None);

        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SaveChangesThrows_RethrowsImmediately()
    {
        // Arrange
        var reaction = new Mock<ISmartsuppWebhookReaction>();
        reaction.Setup(r => r.EventName).Returns("conversation.opened");
        reaction.Setup(r => r.HandleAsync(It.IsAny<WebhookEventContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db error"));

        var handler = CreateHandler(new[] { reaction.Object });

        // Act
        var act = async () => await handler.Handle(MakeRequest("conversation.opened"), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _metrics.Verify(m => m.RecordReceived("conversation.opened", "error", It.IsAny<double>()), Times.Once);
    }
}
