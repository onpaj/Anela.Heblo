using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
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

    private static DbUpdateException MakeUniqueViolation() =>
        new("duplicate key", new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation));

    private static DbUpdateException MakeFkViolation() =>
        new("fk violation", new PostgresException(
            "foreign key violation",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.ForeignKeyViolation));

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
    public async Task Handle_UniqueViolationOnFirstSave_RetriesOnce_AndReturnsHandled()
    {
        // Arrange
        var reaction = new Mock<ISmartsuppWebhookReaction>();
        reaction.Setup(r => r.EventName).Returns("conversation.opened");

        _repo.SetupSequence(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(MakeUniqueViolation())
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(new[] { reaction.Object });

        // Act
        var response = await handler.Handle(MakeRequest("conversation.opened"), CancellationToken.None);

        // Assert
        response.Handled.Should().BeTrue();
        reaction.Verify(r => r.HandleAsync(It.IsAny<WebhookEventContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _repo.Verify(r => r.DiscardChanges(), Times.Once);
        _metrics.Verify(m => m.RecordReceived("conversation.opened", "handled", It.IsAny<double>()), Times.Once);
        _metrics.Verify(m => m.RecordReceived("conversation.opened", "error", It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PersistentUniqueViolation_Rethrows_AfterOneRetry()
    {
        // Arrange
        var reaction = new Mock<ISmartsuppWebhookReaction>();
        reaction.Setup(r => r.EventName).Returns("conversation.opened");
        reaction.Setup(r => r.HandleAsync(It.IsAny<WebhookEventContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(MakeUniqueViolation());

        var handler = CreateHandler(new[] { reaction.Object });

        // Act
        var act = async () => await handler.Handle(MakeRequest("conversation.opened"), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _repo.Verify(r => r.DiscardChanges(), Times.Once);
        _metrics.Verify(m => m.RecordReceived("conversation.opened", "error", It.IsAny<double>()), Times.Once);
        _metrics.Verify(m => m.RecordReceived("conversation.opened", "handled", It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonUniqueDbUpdateException_RethrowsImmediately_WithoutRetry()
    {
        // Arrange
        var reaction = new Mock<ISmartsuppWebhookReaction>();
        reaction.Setup(r => r.EventName).Returns("conversation.opened");
        reaction.Setup(r => r.HandleAsync(It.IsAny<WebhookEventContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(MakeFkViolation());

        var handler = CreateHandler(new[] { reaction.Object });

        // Act
        var act = async () => await handler.Handle(MakeRequest("conversation.opened"), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.DiscardChanges(), Times.Never);
        _metrics.Verify(m => m.RecordReceived("conversation.opened", "error", It.IsAny<double>()), Times.Once);
    }

    [Theory]
    [InlineData("subject", 2500)]
    [InlineData("contact_avatar_url", 2500)]
    [InlineData("contact_name", 300)]
    public async Task Handle_OversizedPayload_DoesNotThrow_AndPersistsTruncatedConversation(
        string fieldName, int oversizeLength)
    {
        var capturedConversations = new List<SmartsuppConversation>();
        var repoStub = new Mock<ISmartsuppRepository>();
        repoStub
            .Setup(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .Callback<SmartsuppConversation, CancellationToken>((c, _) => capturedConversations.Add(c))
            .Returns(Task.CompletedTask);
        repoStub
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var metrics = new Mock<ISmartsuppWebhookMetrics>();
        var reaction = new ConversationOpenedReaction(
            repoStub.Object,
            metrics.Object,
            NullLogger<ConversationOpenedReaction>.Instance);

        var oversized = new string('x', oversizeLength);
        var payload = $@"{{""conversation"":{{""id"":""c-os"",""status"":""open"",""{fieldName}"":""{oversized}"",""created_at"":""2026-06-13T12:25:00Z"",""updated_at"":""2026-06-13T12:25:00Z""}}}}";

        var handler = new ProcessWebhookEventHandler(
            new[] { (ISmartsuppWebhookReaction)reaction },
            repoStub.Object,
            _metrics.Object,
            NullLogger<ProcessWebhookEventHandler>.Instance);

        var response = await handler.Handle(MakeRequest("conversation.opened", payload), CancellationToken.None);

        response.Handled.Should().BeTrue();
        capturedConversations.Should().ContainSingle();
        var conv = capturedConversations[0];
        var entityProperty = fieldName switch
        {
            "subject" => "Subject",
            "contact_avatar_url" => "ContactAvatarUrl",
            "contact_name" => "ContactName",
            _ => throw new InvalidOperationException("unexpected field"),
        };
        var actual = (string?)typeof(SmartsuppConversation).GetProperty(entityProperty)!.GetValue(conv);
        actual.Should().NotBeNull();
        actual!.Length.Should().BeLessThan(oversizeLength);
    }
}
