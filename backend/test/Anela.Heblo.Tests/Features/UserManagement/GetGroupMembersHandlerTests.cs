using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Features.UserManagement.Contracts;

namespace Anela.Heblo.Tests.Features.UserManagement;

public class GetGroupMembersHandlerTests
{
    private readonly Mock<IGraphService> _mockGraphService;
    private readonly Mock<ILogger<GetGroupMembersHandler>> _mockLogger;
    private readonly GetGroupMembersHandler _handler;

    public GetGroupMembersHandlerTests()
    {
        _mockGraphService = new Mock<IGraphService>();
        _mockLogger = new Mock<ILogger<GetGroupMembersHandler>>();
        _handler = new GetGroupMembersHandler(_mockGraphService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WithValidGroupId_ReturnsSuccessfulResponse()
    {
        // Arrange
        var groupId = "test-group-id";
        var request = new GetGroupMembersRequest { GroupId = groupId };
        var expectedMembers = new List<UserDto>
        {
            new UserDto { Id = "1", DisplayName = "John Doe", Email = "john@example.com" },
            new UserDto { Id = "2", DisplayName = "Jane Smith", Email = "jane@example.com" }
        };

        _mockGraphService
            .Setup(x => x.GetGroupMembersAsync(groupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMembers);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Members.Count);
        Assert.Equal("John Doe", result.Members[0].DisplayName);
        Assert.Equal("jane@example.com", result.Members[1].Email);
    }

    [Fact]
    public async Task Handle_WhenGraphServiceThrowsException_ReturnsFailureResponse()
    {
        // Arrange
        var groupId = "test-group-id";
        var request = new GetGroupMembersRequest { GroupId = groupId };

        _mockGraphService
            .Setup(x => x.GetGroupMembersAsync(groupId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Graph API error"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorCode);
        Assert.Empty(result.Members);
    }

    [Fact]
    public async Task Handle_WithEmptyGroupId_CallsGraphService()
    {
        // Arrange
        var groupId = "";
        var request = new GetGroupMembersRequest { GroupId = groupId };
        var expectedMembers = new List<UserDto>();

        _mockGraphService
            .Setup(x => x.GetGroupMembersAsync(groupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMembers);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Members);
        _mockGraphService.Verify(x => x.GetGroupMembersAsync(groupId, It.IsAny<CancellationToken>()), Times.Once);
    }
}