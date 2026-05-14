using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.Mappers;

public class SmartsuppPayloadMapperTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    // --- Conversation mapping ---

    [Theory]
    [InlineData("open", SmartsuppConversationStatus.Open)]
    [InlineData("closed", SmartsuppConversationStatus.Resolved)]
    [InlineData("pending", SmartsuppConversationStatus.Pending)]
    [InlineData("unknown_value", SmartsuppConversationStatus.Open)]
    public void MapConversation_MapsStatus_Correctly(string statusStr, SmartsuppConversationStatus expected)
    {
        var json = $@"{{""id"":""c1"",""status"":""{statusStr}"",""created_at"":""2026-05-13T10:00:00Z"",""updated_at"":""2026-05-13T10:00:00Z""}}";
        var el = Parse(json);

        var result = SmartsuppPayloadMapper.MapConversation(el, DateTime.UtcNow);

        result.Status.Should().Be(expected);
    }

    [Fact]
    public void MapConversation_TruncatesLastMessagePreview_AtTwoHundredChars()
    {
        var longText = new string('x', 250);
        var json = $@"{{""id"":""c1"",""status"":""open"",""last_message_text"":""{longText}"",""created_at"":""2026-05-13T10:00:00Z"",""updated_at"":""2026-05-13T10:00:00Z""}}";

        var result = SmartsuppPayloadMapper.MapConversation(Parse(json), DateTime.UtcNow);

        result.LastMessagePreview.Should().HaveLength(200);
    }

    [Fact]
    public void MapConversation_MapsChannel_FromChannelTypeField()
    {
        var json = @"{""id"":""c1"",""status"":""open"",""channel"":{""type"":""chat""},""created_at"":""2026-05-13T10:00:00Z"",""updated_at"":""2026-05-13T10:00:00Z""}";

        var result = SmartsuppPayloadMapper.MapConversation(Parse(json), DateTime.UtcNow);

        result.Channel.Should().Be("chat");
    }

    [Fact]
    public void MapConversation_HandlesNullOptionalFields_Gracefully()
    {
        var json = @"{""id"":""c1"",""status"":""open"",""contact_id"":null,""visitor_id"":null,""created_at"":""2026-05-13T10:00:00Z"",""updated_at"":""2026-05-13T10:00:00Z""}";

        var act = () => SmartsuppPayloadMapper.MapConversation(Parse(json), DateTime.UtcNow);

        act.Should().NotThrow();
    }

    // --- Message mapping ---

    [Theory]
    [InlineData("agent", SmartsuppMessageAuthorType.Agent)]
    [InlineData("bot", SmartsuppMessageAuthorType.Bot)]
    [InlineData("contact", SmartsuppMessageAuthorType.Visitor)]
    [InlineData("system", SmartsuppMessageAuthorType.System)]
    [InlineData("trigger", SmartsuppMessageAuthorType.Trigger)]
    [InlineData("unknown", SmartsuppMessageAuthorType.Visitor)]
    public void MapMessage_MapsAuthorType_Correctly(string subType, SmartsuppMessageAuthorType expected)
    {
        var json = $@"{{""id"":""m1"",""conversation_id"":""c1"",""sub_type"":""{subType}"",""created_at"":""2026-05-13T10:00:00Z"",""updated_at"":""2026-05-13T10:00:00Z""}}";

        var result = SmartsuppPayloadMapper.MapMessage(Parse(json));

        result.AuthorType.Should().Be(expected);
    }

    [Fact]
    public void MapMessage_ExtractsContentText_FromContentObject()
    {
        var json = @"{""id"":""m1"",""conversation_id"":""c1"",""content"":{""type"":""text"",""text"":""Hello""},""created_at"":""2026-05-13T10:00:00Z"",""updated_at"":""2026-05-13T10:00:00Z""}";

        var result = SmartsuppPayloadMapper.MapMessage(Parse(json));

        result.Content.Should().Be("Hello");
    }

    [Fact]
    public void MapMessage_ExtractsMessageType_FromTypeField()
    {
        var json = @"{""id"":""m1"",""conversation_id"":""c1"",""type"":""note"",""created_at"":""2026-05-13T10:00:00Z"",""updated_at"":""2026-05-13T10:00:00Z""}";

        var result = SmartsuppPayloadMapper.MapMessage(Parse(json));

        result.MessageType.Should().Be("note");
    }

    // --- Contact mapping ---

    [Fact]
    public void MapContact_MapsAllFields_Correctly()
    {
        var json = @"{
            ""id"":""ct1"",
            ""email"":""test@example.com"",
            ""name"":""Test User"",
            ""phone"":""+1234"",
            ""gdpr_approved"":true,
            ""banned_at"":""2026-05-13T10:00:00Z"",
            ""created_at"":""2026-05-13T09:00:00Z"",
            ""updated_at"":""2026-05-13T10:00:00Z""
        }";

        var result = SmartsuppPayloadMapper.MapContact(Parse(json), DateTime.UtcNow);

        result.Id.Should().Be("ct1");
        result.Email.Should().Be("test@example.com");
        result.GdprApproved.Should().BeTrue();
        result.BannedAt.Should().NotBeNull();
    }

    [Fact]
    public void MapContact_SetsNullBannedAt_WhenNotBanned()
    {
        var json = @"{""id"":""ct1"",""created_at"":""2026-05-13T10:00:00Z"",""updated_at"":""2026-05-13T10:00:00Z""}";

        var result = SmartsuppPayloadMapper.MapContact(Parse(json), DateTime.UtcNow);

        result.BannedAt.Should().BeNull();
    }
}
