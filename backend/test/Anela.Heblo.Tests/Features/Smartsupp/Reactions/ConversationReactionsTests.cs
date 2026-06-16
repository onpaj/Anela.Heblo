using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.Reactions;

public class ConversationReactionsTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppWebhookMetrics> _metrics = new();

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static WebhookEventContext MakeCtx(string eventName, string dataJson) =>
        new()
        {
            EventName = eventName,
            Timestamp = DateTime.UtcNow,
            AccountId = "acc-1",
            AppId = "app-1",
            Data = Parse(dataJson),
        };

    private static string ConvJson(string id = "c1", string status = "open") => $@"{{
        ""id"":""{id}"",
        ""status"":""{status}"",
        ""created_at"":""2026-05-13T10:00:00Z"",
        ""updated_at"":""2026-05-13T10:01:00Z""
    }}";

    private static string MsgJson(string id = "m1", string convId = "c1", string subType = "agent") => $@"{{
        ""id"":""{id}"",
        ""conversation_id"":""{convId}"",
        ""sub_type"":""{subType}"",
        ""content"":{{""type"":""text"",""text"":""Hello""}},
        ""created_at"":""2026-05-13T10:00:00Z"",
        ""updated_at"":""2026-05-13T10:00:00Z""
    }}";

    [Fact]
    public async Task ConversationOpenedReaction_UpsertsConversation()
    {
        var reaction = new ConversationOpenedReaction(_repo.Object, _metrics.Object, NullLogger<ConversationOpenedReaction>.Instance);
        var ctx = MakeCtx("conversation.opened", $@"{{""conversation"":{ConvJson()}}}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ConversationOpenedReaction_HasCorrectEventName()
    {
        var reaction = new ConversationOpenedReaction(_repo.Object, _metrics.Object, NullLogger<ConversationOpenedReaction>.Instance);
        reaction.EventName.Should().Be("conversation.opened");
    }

    [Fact]
    public async Task ConversationClosedReaction_UpsertsConversationWithCloseType()
    {
        var reaction = new ConversationClosedReaction(_repo.Object, _metrics.Object, NullLogger<ConversationClosedReaction>.Instance);
        var ctx = MakeCtx("conversation.closed", $@"{{
            ""conversation"":{ConvJson(status: "closed")},
            ""close_type"":""agent"",
            ""agent_id"":""123""
        }}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c =>
                c.Id == "c1" &&
                c.CloseType == "agent" &&
                c.ClosedByAgentId == "123" &&
                c.LastClosedAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConversationClosedByContactReaction_UpsertsConversation_WithContactCloseType()
    {
        var reaction = new ConversationClosedByContactReaction(_repo.Object, _metrics.Object, NullLogger<ConversationClosedByContactReaction>.Instance);
        var ctx = MakeCtx("conversation.closed_by_contact", $@"{{""conversation"":{ConvJson()}}}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.CloseType == "contact"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConversationContactRepliedReaction_UpsertsConversationAndMessage()
    {
        var reaction = new ConversationContactRepliedReaction(_repo.Object, _metrics.Object, NullLogger<ConversationContactRepliedReaction>.Instance);
        var ctx = MakeCtx("conversation.contact_replied", $@"{{
            ""conversation"":{ConvJson()},
            ""message"":{MsgJson(subType: "contact")}
        }}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c1"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertMessagesAsync("c1", It.Is<List<SmartsuppMessage>>(msgs => msgs.Count == 1 && msgs[0].Id == "m1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConversationAgentRepliedReaction_UpsertsConversationAndMessage()
    {
        var reaction = new ConversationAgentRepliedReaction(_repo.Object, _metrics.Object, NullLogger<ConversationAgentRepliedReaction>.Instance);
        var ctx = MakeCtx("conversation.agent_replied", $@"{{
            ""conversation"":{ConvJson()},
            ""message"":{MsgJson(subType: "agent")}
        }}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertMessagesAsync("c1", It.IsAny<List<SmartsuppMessage>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConversationBotRepliedReaction_UpsertsConversationAndMessage()
    {
        var reaction = new ConversationBotRepliedReaction(_repo.Object, _metrics.Object, NullLogger<ConversationBotRepliedReaction>.Instance);
        var ctx = MakeCtx("conversation.bot_replied", $@"{{
            ""conversation"":{ConvJson()},
            ""message"":{MsgJson(subType: "bot")}
        }}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertMessagesAsync("c1", It.IsAny<List<SmartsuppMessage>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConversationAgentAssignedReaction_UpsertsConversationWithAssignedAgent()
    {
        var reaction = new ConversationAgentAssignedReaction(_repo.Object, _metrics.Object, NullLogger<ConversationAgentAssignedReaction>.Instance);
        var ctx = MakeCtx("conversation.agent_assigned", $@"{{
            ""conversation"":{ConvJson()},
            ""assigned"":""456"",
            ""assigned_by"":""789""
        }}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConversationAgentUnassignedReaction_UpsertsConversation()
    {
        var reaction = new ConversationAgentUnassignedReaction(_repo.Object, _metrics.Object, NullLogger<ConversationAgentUnassignedReaction>.Instance);
        var ctx = MakeCtx("conversation.agent_unassigned", $@"{{
            ""conversation"":{ConvJson()},
            ""unassigned"":""456"",
            ""unassigned_by"":""789""
        }}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConversationAgentJoinedReaction_DoesNotCallRepository()
    {
        var reaction = new ConversationAgentJoinedReaction();
        var ctx = MakeCtx("conversation.agent_joined", $@"{{""conversation"":{ConvJson()},""agent_id"":""123""}}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConversationAgentLeftReaction_DoesNotCallRepository()
    {
        var reaction = new ConversationAgentLeftReaction();
        var ctx = MakeCtx("conversation.agent_left", $@"{{""conversation"":{ConvJson()},""agent_id"":""123""}}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConversationRatedReaction_UpsertsConversationWithRating()
    {
        var reaction = new ConversationRatedReaction(_repo.Object, _metrics.Object, NullLogger<ConversationRatedReaction>.Instance);
        var ctx = MakeCtx("conversation.rated", $@"{{
            ""conversation"":{ConvJson()},
            ""rating_value"":5,
            ""rating_text"":""Great support!""
        }}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Rating == 5 && c.RatingText == "Great support!"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConversationMessageDeliveredReaction_UpdatesDeliveryStatus()
    {
        var reaction = new ConversationMessageDeliveredReaction(_repo.Object);
        var ctx = MakeCtx("conversation.message_delivered", $@"{{
            ""conversation"":{ConvJson()},
            ""message"":{MsgJson()}
        }}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.Verify(r => r.UpdateMessageDeliveryStatusAsync("m1", "delivered", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConversationMessageDeliveryFailedReaction_UpdatesDeliveryStatusToFailed()
    {
        var reaction = new ConversationMessageDeliveryFailedReaction(_repo.Object);
        var ctx = MakeCtx("conversation.message_delivery_failed", $@"{{
            ""conversation"":{ConvJson()},
            ""message"":{MsgJson()}
        }}");

        await reaction.HandleAsync(ctx, CancellationToken.None);

        _repo.Verify(r => r.UpdateMessageDeliveryStatusAsync("m1", "failed", null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
