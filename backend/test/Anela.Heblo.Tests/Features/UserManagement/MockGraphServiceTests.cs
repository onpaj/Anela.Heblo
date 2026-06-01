using Anela.Heblo.Application.Features.UserManagement.Services;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.UserManagement;

public class MockGraphServiceTests
{
    private readonly Mock<ILogger<MockGraphService>> _loggerMock;
    private readonly MockGraphService _service;

    public MockGraphServiceTests()
    {
        _loggerMock = new Mock<ILogger<MockGraphService>>();
        _service = new MockGraphService(_loggerMock.Object);
    }

    [Fact]
    public async Task GetGroupMembersAsync_ReturnsExpectedMockUsers()
    {
        // Arrange
        var groupId = "test-group-id";

        // Act
        var result = await _service.GetGroupMembersAsync(groupId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);

        result[0].Id.Should().Be("mock-user-1");
        result[0].DisplayName.Should().Be("Mock User 1");
        result[0].Email.Should().Be("mock.user1@anela-heblo.com");

        result[1].Id.Should().Be("mock-user-2");
        result[1].DisplayName.Should().Be("Mock User 2");
        result[1].Email.Should().Be("mock.user2@anela-heblo.com");

        result[2].Id.Should().Be("mock-user-3");
        result[2].DisplayName.Should().Be("Mock Administrator");
        result[2].Email.Should().Be("mock.admin@anela-heblo.com");
    }

    [Fact]
    public async Task GetGroupMembersAsync_LogsCorrectMessage()
    {
        // Arrange
        var groupId = "test-group-id";

        // Act
        await _service.GetGroupMembersAsync(groupId);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Mock GraphService: Returning mock group members for group {groupId}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}