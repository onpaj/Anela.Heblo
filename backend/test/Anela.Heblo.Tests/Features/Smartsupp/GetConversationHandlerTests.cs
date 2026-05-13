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

        var handler = new GetConversationHandler(_repo.Object);
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
        var handler = new GetConversationHandler(_repo.Object);

        // Act
        var result = await handler.Handle(new GetConversationRequest { Id = "missing" }, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationNotFound);
    }
}
