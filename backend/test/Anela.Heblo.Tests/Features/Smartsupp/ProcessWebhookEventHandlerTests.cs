using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class ProcessWebhookEventHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();

    private ProcessWebhookEventHandler CreateHandler() =>
        new(_repo.Object, NullLogger<ProcessWebhookEventHandler>.Instance);

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static ProcessWebhookEventRequest MakeRequest(string eventName, string dataJson) =>
        new()
        {
            EventName = eventName,
            Timestamp = DateTime.UtcNow,
            AccountId = "acc-1",
            AppId = "app-1",
            Data = Parse(dataJson),
        };

    [Fact]
    public async Task Handle_ConversationCreated_UpsertsConversationAndSaves()
    {
        var data = """
            {
              "id": "c1",
              "status": "open",
              "unread": false,
              "is_offline": false,
              "is_served": false,
              "contact_id": null,
              "visitor_id": null,
              "created_at": "2026-05-13T10:00:00Z",
              "updated_at": "2026-05-13T10:00:00Z"
            }
            """;
        var request = MakeRequest("conversation.created", data);

        var response = await CreateHandler().Handle(request, CancellationToken.None);

        response.Handled.Should().BeTrue();
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c1" && c.Status == SmartsuppConversationStatus.Open),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ConversationUpdated_UpsertsConversation()
    {
        var data = """
            {
              "id": "c1",
              "status": "open",
              "created_at": "2026-05-13T10:00:00Z",
              "updated_at": "2026-05-13T10:05:00Z"
            }
            """;
        var request = MakeRequest("conversation.updated", data);

        var response = await CreateHandler().Handle(request, CancellationToken.None);

        response.Handled.Should().BeTrue();
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ConversationClosed_MapsResolvedStatus()
    {
        var data = """
            {
              "id": "c1",
              "status": "resolved",
              "created_at": "2026-05-13T10:00:00Z",
              "updated_at": "2026-05-13T11:00:00Z"
            }
            """;
        var request = MakeRequest("conversation.closed", data);

        var response = await CreateHandler().Handle(request, CancellationToken.None);

        response.Handled.Should().BeTrue();
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c1" && c.Status == SmartsuppConversationStatus.Resolved),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MessageCreated_UpsertsMessageOnly()
    {
        var data = """
            {
              "id": "m1",
              "conversation_id": "c1",
              "sub_type": "contact",
              "content": { "text": "Hello", "type": "text" },
              "created_at": "2026-05-13T10:00:00Z",
              "updated_at": "2026-05-13T10:00:00Z"
            }
            """;
        var request = MakeRequest("message.created", data);

        var response = await CreateHandler().Handle(request, CancellationToken.None);

        response.Handled.Should().BeTrue();
        _repo.Verify(r => r.UpsertMessagesAsync(
            "c1",
            It.Is<List<SmartsuppMessage>>(msgs =>
                msgs.Count == 1
                && msgs[0].Id == "m1"
                && msgs[0].AuthorType == SmartsuppMessageAuthorType.Visitor),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownEvent_ReturnsHandledFalse_WithReason()
    {
        var request = MakeRequest("conversation.exploded", "{}");

        var response = await CreateHandler().Handle(request, CancellationToken.None);

        response.Handled.Should().BeFalse();
        response.Reason.Should().Be("unknown event");
        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.UpsertMessagesAsync(It.IsAny<string>(), It.IsAny<List<SmartsuppMessage>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
