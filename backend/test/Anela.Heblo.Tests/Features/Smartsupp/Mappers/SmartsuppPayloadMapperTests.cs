using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.Mappers;

public class SmartsuppPayloadMapperTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static readonly ILogger Logger = NullLogger.Instance;
    private static readonly Mock<ISmartsuppWebhookMetrics> MetricsMock = new();
    private static ISmartsuppWebhookMetrics Metrics => MetricsMock.Object;

    private static SmartsuppConversation Map(JsonElement el) =>
        SmartsuppPayloadMapper.MapConversation(el, DateTime.UtcNow, Logger, Metrics);

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

        var result = Map(el);

        result.Status.Should().Be(expected);
    }

    [Fact]
    public void MapConversation_TruncatesLastMessagePreview_AtTwoHundredChars()
    {
        var longText = new string('x', 250);
        var json = $@"{{""id"":""c1"",""status"":""open"",""last_message_text"":""{longText}"",""created_at"":""2026-05-13T10:00:00Z"",""updated_at"":""2026-05-13T10:00:00Z""}}";

        var result = SmartsuppPayloadMapper.MapConversation(Parse(json), DateTime.UtcNow, Logger, Metrics);

        result.LastMessagePreview.Should().HaveLength(200);
    }

    [Fact]
    public void MapConversation_MapsChannel_FromChannelTypeField()
    {
        var json = @"{""id"":""c1"",""status"":""open"",""channel"":{""type"":""chat""},""created_at"":""2026-05-13T10:00:00Z"",""updated_at"":""2026-05-13T10:00:00Z""}";

        var result = SmartsuppPayloadMapper.MapConversation(Parse(json), DateTime.UtcNow, Logger, Metrics);

        result.Channel.Should().Be("chat");
    }

    [Fact]
    public void MapConversation_HandlesNullOptionalFields_Gracefully()
    {
        var json = @"{""id"":""c1"",""status"":""open"",""contact_id"":null,""visitor_id"":null,""created_at"":""2026-05-13T10:00:00Z"",""updated_at"":""2026-05-13T10:00:00Z""}";

        var act = () => SmartsuppPayloadMapper.MapConversation(Parse(json), DateTime.UtcNow, Logger, Metrics);

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

    // --- Conversation field truncation ---

    [Fact]
    public void MapConversation_TruncatesSubject_AtTwoThousandChars()
    {
        var longSubject = new string('s', 2500);
        var json = $@"{{""id"":""c1"",""status"":""open"",""subject"":""{longSubject}"",""created_at"":""2026-06-13T12:25:00Z"",""updated_at"":""2026-06-13T12:25:00Z""}}";

        var result = Map(Parse(json));

        result.Subject.Should().HaveLength(2000);
    }

    [Fact]
    public void MapConversation_TruncatesContactAvatarUrl_AtTwoThousandChars()
    {
        var longUrl = "https://cdn.example.com/" + new string('a', 3000);
        var json = $@"{{""id"":""c1"",""status"":""open"",""contact_avatar_url"":""{longUrl}"",""created_at"":""2026-06-13T12:25:00Z"",""updated_at"":""2026-06-13T12:25:00Z""}}";

        var result = Map(Parse(json));

        result.ContactAvatarUrl.Should().HaveLength(2000);
    }

    [Fact]
    public void MapConversation_DoesNotTruncateReferer_BecauseColumnIsUnbounded()
    {
        var longReferer = "https://referer.example.com/?" + new string('q', 5000);
        var json = $@"{{""id"":""c1"",""status"":""open"",""referer"":""{longReferer}"",""created_at"":""2026-06-13T12:25:00Z"",""updated_at"":""2026-06-13T12:25:00Z""}}";

        var result = Map(Parse(json));

        result.Referer.Should().Be(longReferer);
    }

    [Theory]
    [InlineData("contact_name", 200, "ContactName")]
    [InlineData("contact_email", 200, "ContactEmail")]
    [InlineData("domain", 200, "Domain")]
    [InlineData("location_country", 100, "LocationCountry")]
    [InlineData("location_city", 100, "LocationCity")]
    [InlineData("location_ip", 50, "LocationIp")]
    [InlineData("location_code", 10, "LocationCode")]
    [InlineData("close_type", 50, "CloseType")]
    [InlineData("rating_text", 1000, "RatingText")]
    public void MapConversation_TruncatesBoundedField_AtItsOwnLimit(
        string jsonField, int expectedLength, string entityProperty)
    {
        var raw = new string('z', expectedLength + 50);
        var json = $@"{{""id"":""c1"",""status"":""open"",""{jsonField}"":""{raw}"",""created_at"":""2026-06-13T12:25:00Z"",""updated_at"":""2026-06-13T12:25:00Z""}}";

        var result = Map(Parse(json));

        var actual = (string?)typeof(SmartsuppConversation)
            .GetProperty(entityProperty)!
            .GetValue(result);
        actual.Should().HaveLength(expectedLength);
    }

    [Fact]
    public void MapConversation_PassesShortStrings_Unchanged()
    {
        var json = @"{""id"":""c1"",""status"":""open"",""subject"":""hi"",""contact_name"":""Jana"",""referer"":""https://shop.example.com/"",""created_at"":""2026-06-13T12:25:00Z"",""updated_at"":""2026-06-13T12:25:00Z""}";

        var result = Map(Parse(json));

        result.Subject.Should().Be("hi");
        result.ContactName.Should().Be("Jana");
        result.Referer.Should().Be("https://shop.example.com/");
    }

    [Fact]
    public void MapConversation_TruncationLogsConversationId_AsContextId()
    {
        var capturingLogger = new CapturingLogger();
        var longSubject = new string('s', 2500);
        var json = $@"{{""id"":""c-abc"",""status"":""open"",""subject"":""{longSubject}"",""created_at"":""2026-06-13T12:25:00Z"",""updated_at"":""2026-06-13T12:25:00Z""}}";

        SmartsuppPayloadMapper.MapConversation(Parse(json), DateTime.UtcNow, capturingLogger, Metrics);

        capturingLogger.Warnings.Should().Contain(w => w.Contains("c-abc"));
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Warnings { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
