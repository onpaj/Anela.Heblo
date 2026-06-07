using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Shared;
using MediatR;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class UserManagementMcpToolsTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly UserManagementMcpTools _tools;

    public UserManagementMcpToolsTests()
    {
        _tools = new UserManagementMcpTools(_mediatorMock.Object);
    }

    [Fact]
    public async Task GetGroupMembers_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expected = new GetGroupMembersResponse { Success = true };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetGroupMembersRequest>(), default))
            .ReturnsAsync(expected);

        // Act
        var json = await _tools.GetGroupMembers("group-id-123");

        // Assert
        _mediatorMock.Verify(
            m => m.Send(It.Is<GetGroupMembersRequest>(r => r.GroupId == "group-id-123"), default),
            Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetGroupMembersResponse>(json);
        Assert.NotNull(deserialized);
        Assert.True(deserialized!.Success);
    }

    [Fact]
    public async Task GetGroupMembers_ShouldThrowMcpException_WhenExternalServiceFails()
    {
        // Arrange
        var failed = new GetGroupMembersResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ExternalServiceError,
            Params = new Dictionary<string, string> { { "GroupId", "group-id-999" } }
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetGroupMembersRequest>(), default))
            .ReturnsAsync(failed);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<McpException>(() => _tools.GetGroupMembers("group-id-999"));
        Assert.Contains("ExternalServiceError", ex.Message);
    }
}
