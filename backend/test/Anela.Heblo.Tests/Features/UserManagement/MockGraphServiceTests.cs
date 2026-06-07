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
    public async Task GetGroupMembersAsync_ReturnsEmptyList()
    {
        var result = await _service.GetGroupMembersAsync("test-group-id");

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGroupMembersAsync_LogsCorrectMessage()
    {
        var groupId = "test-group-id";

        await _service.GetGroupMembersAsync(groupId);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Mock GraphService: GetGroupMembersAsync called for group {groupId}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}