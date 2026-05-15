using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class ConversationTranscriptBuilderTests
{
    private static SmartsuppMessage Msg(string id, SmartsuppMessageAuthorType type, string? content, int minuteOffset) =>
        new()
        {
            Id = id,
            ConversationId = "c1",
            AuthorType = type,
            Content = content,
            CreatedAt = new DateTime(2026, 5, 15, 10, minuteOffset, 0, DateTimeKind.Utc)
        };

    [Fact]
    public void Build_OrdersByCreatedAt_AndRoleLabelsLines()
    {
        var messages = new List<SmartsuppMessage>
        {
            Msg("m2", SmartsuppMessageAuthorType.Agent, "Dobrý den", 2),
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Mám dotaz", 1),
        };

        var result = ConversationTranscriptBuilder.Build(messages);

        result.Should().Be("Zákazník: Mám dotaz\nAgent: Dobrý den");
    }

    [Fact]
    public void Build_SkipsSystemTriggerAndEmptyMessages()
    {
        var messages = new List<SmartsuppMessage>
        {
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "Ahoj", 1),
            Msg("m2", SmartsuppMessageAuthorType.System, "připojen agent", 2),
            Msg("m3", SmartsuppMessageAuthorType.Trigger, "uvítací zpráva", 3),
            Msg("m4", SmartsuppMessageAuthorType.Agent, "   ", 4),
            Msg("m5", SmartsuppMessageAuthorType.Bot, "Bot odpověď", 5),
        };

        var result = ConversationTranscriptBuilder.Build(messages);

        result.Should().Be("Zákazník: Ahoj\nBot: Bot odpověď");
    }

    [Fact]
    public void LastContactMessages_ReturnsLastThreeVisitorMessagesJoined()
    {
        var messages = new List<SmartsuppMessage>
        {
            Msg("m1", SmartsuppMessageAuthorType.Visitor, "první", 1),
            Msg("m2", SmartsuppMessageAuthorType.Visitor, "druhá", 2),
            Msg("m3", SmartsuppMessageAuthorType.Agent, "agent", 3),
            Msg("m4", SmartsuppMessageAuthorType.Visitor, "třetí", 4),
            Msg("m5", SmartsuppMessageAuthorType.Visitor, "čtvrtá", 5),
        };

        var result = ConversationTranscriptBuilder.LastContactMessages(messages);

        result.Should().Be("druhá\ntřetí\nčtvrtá");
    }

    [Fact]
    public void LastContactMessages_ReturnsNull_WhenNoVisitorMessages()
    {
        var messages = new List<SmartsuppMessage>
        {
            Msg("m1", SmartsuppMessageAuthorType.Agent, "agent", 1),
        };

        ConversationTranscriptBuilder.LastContactMessages(messages).Should().BeNull();
    }
}
