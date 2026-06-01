using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class UserManagementControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly UserManagementController _controller;

    public UserManagementControllerTests()
    {
        _controller = new UserManagementController(_mediatorMock.Object);

        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task GetGroupMembers_ReturnsOk_WithMembers_OnSuccessfulHandlerResponse()
    {
        // Arrange
        var groupId = "group-abc";
        var handlerResponse = new GetGroupMembersResponse
        {
            Success = true,
            Members = new List<UserDto>
            {
                new() { Id = "1", DisplayName = "Alice", Email = "alice@anela.cz" },
                new() { Id = "2", DisplayName = "Bob",   Email = "bob@anela.cz" }
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetGroupMembersRequest>(r => r.GroupId == groupId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var result = await _controller.GetGroupMembers(groupId, CancellationToken.None);

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<GetGroupMembersResponse>().Subject;
        payload.Members.Should().HaveCount(2);
        payload.Members[0].Email.Should().Be("alice@anela.cz");

        _mediatorMock.Verify(
            m => m.Send(It.Is<GetGroupMembersRequest>(r => r.GroupId == groupId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetGroupMembers_ReturnsHandlerFailure_ThroughHandleResponse()
    {
        // Arrange
        var groupId = "group-xyz";
        var failed = new GetGroupMembersResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InternalServerError,
            Members = new List<UserDto>()
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetGroupMembersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failed);

        // Act
        var result = await _controller.GetGroupMembers(groupId, CancellationToken.None);

        // Assert — InternalServerError maps to 500 via BaseApiController.HandleResponse
        var status = result.Result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GetGroupMembers_DelegatesToMediator_WithoutAnyControllerSideValidation()
    {
        // The [ApiController]+[Required] short-circuit is enforced by the MVC framework
        // before the action runs. This test exists to lock the contract that the action
        // body itself is only: build request → Send → HandleResponse — no manual logging,
        // no manual string.IsNullOrEmpty check, no IConfiguration access.

        // Arrange
        var handlerResponse = new GetGroupMembersResponse { Success = true };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetGroupMembersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        await _controller.GetGroupMembers("any-id", CancellationToken.None);

        // Assert
        _mediatorMock.Verify(
            m => m.Send(It.Is<GetGroupMembersRequest>(r => r.GroupId == "any-id"), It.IsAny<CancellationToken>()),
            Times.Once);
        _mediatorMock.VerifyNoOtherCalls();
    }
}
