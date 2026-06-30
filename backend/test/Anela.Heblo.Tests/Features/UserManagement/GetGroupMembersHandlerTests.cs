using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Shared;

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
        Assert.Equal(ErrorCodes.InternalServerError, result.ErrorCode);
        Assert.Empty(result.Members);
    }

    [Fact]
    public async Task Handle_WhenGraphServiceThrowsAuthException_ReturnsConfigurationError()
    {
        // Arrange
        var groupId = "test-group-id";
        var request = new GetGroupMembersRequest { GroupId = groupId };
        _mockGraphService
            .Setup(x => x.GetGroupMembersAsync(groupId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GraphServiceAuthException("Token acquisition failed", new Exception()));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ConfigurationError, result.ErrorCode);
        Assert.Empty(result.Members);
    }

    [Fact]
    public async Task Handle_WhenGraphServiceThrowsServiceException_ReturnsExternalServiceError()
    {
        // Arrange
        var groupId = "test-group-id";
        var request = new GetGroupMembersRequest { GroupId = groupId };
        _mockGraphService
            .Setup(x => x.GetGroupMembersAsync(groupId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GraphServiceException("Graph API error", new Exception()));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ExternalServiceError, result.ErrorCode);
        Assert.Empty(result.Members);
    }

    [Fact]
    public async Task Handle_WhenGraphServiceThrowsUnauthorizedAccessException_ReturnsForbidden()
    {
        // Arrange
        var groupId = "test-group-id";
        var request = new GetGroupMembersRequest { GroupId = groupId };
        _mockGraphService
            .Setup(x => x.GetGroupMembersAsync(groupId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Access denied"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.Forbidden, result.ErrorCode);
        Assert.Empty(result.Members);
    }

    [Fact]
    public async Task Handle_WhenGroupIsEmpty_ReturnsSuccessWithEmptyMembers()
    {
        // Arrange
        var groupId = "test-group-id";
        var request = new GetGroupMembersRequest { GroupId = groupId };
        _mockGraphService
            .Setup(x => x.GetGroupMembersAsync(groupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Members);
        Assert.Null(result.ErrorCode);
    }
}