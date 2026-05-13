using Anela.Heblo.Application.Features.Smartsupp.UseCases.ListConversations;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class ListConversationsHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();

    [Fact]
    public async Task Handle_ReturnsPagedConversations_ForOpenStatus()
    {
        // Arrange
        var conversations = new List<SmartsuppConversation>
        {
            new() { Id = "c1", Status = SmartsuppConversationStatus.Open, ContactName = "Jan Novák", LastMessageAt = DateTime.UtcNow }
        };
        _repo.Setup(r => r.ListConversationsAsync(SmartsuppConversationStatus.Open, 1, 50, default))
             .ReturnsAsync((conversations, 1));

        var handler = new ListConversationsHandler(_repo.Object);
        var request = new ListConversationsRequest { Status = "Open", Page = 1, PageSize = 50 };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be("c1");
        result.Total.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenUsingDefaultRequest()
    {
        // Arrange
        _repo.Setup(r => r.ListConversationsAsync(SmartsuppConversationStatus.Open, 1, 50, default))
             .ReturnsAsync((new List<SmartsuppConversation>(), 0));

        var handler = new ListConversationsHandler(_repo.Object);
        var request = new ListConversationsRequest();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        _repo.Verify(r => r.ListConversationsAsync(SmartsuppConversationStatus.Open, 1, 50, default), Times.Once);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DefaultsToOpenStatus_WhenStatusIsUnparseable()
    {
        // Arrange
        _repo.Setup(r => r.ListConversationsAsync(SmartsuppConversationStatus.Open, 1, 50, default))
             .ReturnsAsync((new List<SmartsuppConversation>(), 0));

        var handler = new ListConversationsHandler(_repo.Object);
        var request = new ListConversationsRequest { Status = "garbage", Page = 1, PageSize = 50 };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        _repo.Verify(r => r.ListConversationsAsync(SmartsuppConversationStatus.Open, 1, 50, default), Times.Once);
        result.Success.Should().BeTrue();
    }
}
