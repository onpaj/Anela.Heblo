using Anela.Heblo.Application.Features.Smartsupp;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetConversation;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class GetConversationHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppAgentCache> _agentCache = new();

    public GetConversationHandlerTests()
    {
        _agentCache
            .Setup(c => c.GetAgentNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
    }

    [Fact]
    public async Task Handle_ReturnsConversationWithMessages_WhenFound()
    {
        // Arrange
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            ContactName = "Jan",
            Status = SmartsuppConversationStatus.Open,
            Messages = new List<SmartsuppMessage>
            {
                new() { Id = "m1", ConversationId = "c1", Content = "Ahoj", AuthorType = SmartsuppMessageAuthorType.Visitor, CreatedAt = DateTime.UtcNow }
            }
        };
        _repo.Setup(r => r.GetConversationAsync("c1", default)).ReturnsAsync(conversation);

        var handler = new GetConversationHandler(_repo.Object, _agentCache.Object);
        var request = new GetConversationRequest { Id = "c1" };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Conversation.Should().NotBeNull();
        result.Conversation!.Id.Should().Be("c1");
        result.Messages.Should().HaveCount(1);
        result.Messages[0].Content.Should().Be("Ahoj");
    }

    [Fact]
    public async Task Handle_ReturnsError_WhenNotFound()
    {
        // Arrange
        _repo.Setup(r => r.GetConversationAsync("missing", default)).ReturnsAsync((SmartsuppConversation?)null);
        var handler = new GetConversationHandler(_repo.Object, _agentCache.Object);

        // Act
        var result = await handler.Handle(new GetConversationRequest { Id = "missing" }, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationNotFound);
    }

    [Fact]
    public async Task Handle_PopulatesContactFields_WhenContactIsLoaded()
    {
        // Arrange
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            ContactId = "contact-1",
            ContactName = "Jana",
            Status = SmartsuppConversationStatus.Open,
            LocationIp = "1.2.3.4",
            VariablesJson = """{"shoptet_shop":"anela","cart_value":"250"}""",
            Messages = new List<SmartsuppMessage>(),
            Contact = new SmartsuppContact
            {
                Id = "contact-1",
                Name = "Jana",
                Phone = "+420 600 123 456",
                Note = "VIP zákazník",
                TagsJson = """["vip","stala"]""",
                PropertiesJson = """{"shoptet_guid":"abc-123","membership":"gold"}""",
            },
        };
        _repo.Setup(r => r.GetConversationAsync("c1", default)).ReturnsAsync(conversation);
        _repo.Setup(r => r.ListConversationsForContactAsync("contact-1", "c1", default))
             .ReturnsAsync(new List<SmartsuppConversation>());

        var handler = new GetConversationHandler(_repo.Object, _agentCache.Object);

        // Act
        var result = await handler.Handle(new GetConversationRequest { Id = "c1" }, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var dto = result.Conversation!;
        dto.ContactPhone.Should().Be("+420 600 123 456");
        dto.ContactNote.Should().Be("VIP zákazník");
        dto.ContactTags.Should().BeEquivalentTo(new[] { "vip", "stala" });
        dto.ContactProperties.Should().ContainKey("shoptet_guid").WhoseValue.Should().Be("abc-123");
        dto.ContactProperties.Should().ContainKey("membership").WhoseValue.Should().Be("gold");
        dto.LocationIp.Should().Be("1.2.3.4");
        dto.Variables.Should().ContainKey("shoptet_shop").WhoseValue.Should().Be("anela");
        dto.Variables.Should().ContainKey("cart_value").WhoseValue.Should().Be("250");
    }

    [Fact]
    public async Task Handle_PopulatesOtherConversations_WhenContactHasSiblings()
    {
        // Arrange
        var sibling = new SmartsuppConversation
        {
            Id = "c2",
            Status = SmartsuppConversationStatus.Resolved,
            IsUnread = false,
            LastMessageAt = new DateTime(2026, 5, 10, 10, 0, 0, DateTimeKind.Unspecified),
            LastMessagePreview = "Děkuji",
            CreatedAt = new DateTime(2026, 5, 10, 9, 0, 0, DateTimeKind.Unspecified),
            UpdatedAt = new DateTime(2026, 5, 10, 10, 0, 0, DateTimeKind.Unspecified),
            SyncedAt = new DateTime(2026, 5, 10, 10, 0, 0, DateTimeKind.Unspecified),
        };
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            ContactId = "contact-1",
            Status = SmartsuppConversationStatus.Open,
            Messages = new List<SmartsuppMessage>(),
            Contact = new SmartsuppContact { Id = "contact-1" },
        };
        _repo.Setup(r => r.GetConversationAsync("c1", default)).ReturnsAsync(conversation);
        _repo.Setup(r => r.ListConversationsForContactAsync("contact-1", "c1", default))
             .ReturnsAsync(new List<SmartsuppConversation> { sibling });

        var handler = new GetConversationHandler(_repo.Object, _agentCache.Object);

        // Act
        var result = await handler.Handle(new GetConversationRequest { Id = "c1" }, CancellationToken.None);

        // Assert
        result.Conversation!.OtherConversations.Should().HaveCount(1);
        var other = result.Conversation.OtherConversations[0];
        other.Id.Should().Be("c2");
        other.Status.Should().Be("Resolved");
        other.LastMessagePreview.Should().Be("Děkuji");
        other.IsUnread.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PopulatesAgentNames_FromCache()
    {
        // Arrange
        var cache = new Mock<ISmartsuppAgentCache>();
        cache.Setup(c => c.GetAgentNamesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Dictionary<string, string> { { "12", "Ondra" }, { "11", "Jana" } });
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            Messages = new List<SmartsuppMessage>(),
        };
        _repo.Setup(r => r.GetConversationAsync("c1", default)).ReturnsAsync(conversation);
        var handler = new GetConversationHandler(_repo.Object, cache.Object);

        // Act
        var result = await handler.Handle(new GetConversationRequest { Id = "c1" }, CancellationToken.None);

        // Assert
        result.AgentNames.Should().ContainKey("12").WhoseValue.Should().Be("Ondra");
        result.AgentNames.Should().ContainKey("11").WhoseValue.Should().Be("Jana");
    }

    [Fact]
    public async Task Handle_Variables_EmptyOnInvalidJson()
    {
        // Arrange
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            VariablesJson = "not-valid-json",
            Messages = new List<SmartsuppMessage>(),
        };
        _repo.Setup(r => r.GetConversationAsync("c1", default)).ReturnsAsync(conversation);
        var handler = new GetConversationHandler(_repo.Object, _agentCache.Object);

        // Act
        var result = await handler.Handle(new GetConversationRequest { Id = "c1" }, CancellationToken.None);

        // Assert — must not throw; Variables must be empty
        result.Success.Should().BeTrue();
        result.Conversation!.Variables.Should().BeEmpty();
    }
}
