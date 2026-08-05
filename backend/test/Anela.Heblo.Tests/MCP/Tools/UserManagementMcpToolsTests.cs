using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class UserManagementMcpToolsTests
{
    private static readonly string ReadRole = AccessRoles.For(Feature.Admin_Administration, AccessLevel.Read);

    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly UserManagementMcpTools _tools;

    public UserManagementMcpToolsTests()
    {
        _currentUserServiceMock.Setup(s => s.IsInRole(ReadRole)).Returns(true);
        _tools = new UserManagementMcpTools(_mediatorMock.Object, _currentUserServiceMock.Object);
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

        var deserialized = JsonSerializer.Deserialize<GetGroupMembersResponse>(json, McpJsonOptions.Default);
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

    [Fact]
    public async Task GetGroupMembers_ThrowsForbidden_AndSkipsMediator_WhenUserLacksReadRole()
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.IsInRole(ReadRole)).Returns(false);

        // Act
        var exception = await Assert.ThrowsAsync<McpException>(() => _tools.GetGroupMembers("group-id-123"));

        // Assert
        Assert.Contains("FORBIDDEN", exception.Message);
        Assert.Contains(ReadRole, exception.Message);
        _mediatorMock.Verify(m => m.Send(It.IsAny<GetGroupMembersRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
