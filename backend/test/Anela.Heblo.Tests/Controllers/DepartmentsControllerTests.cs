using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetDepartments;
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

public class DepartmentsControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly DepartmentsController _controller;

    public DepartmentsControllerTests()
    {
        _controller = new DepartmentsController(_mediatorMock.Object);

        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task GetDepartments_ReturnsOk_WithDepartments_OnSuccessfulHandlerResponse()
    {
        // Arrange
        var handlerResponse = new GetDepartmentsResponse
        {
            Success = true,
            Departments = new List<DepartmentDto>
            {
                new() { Id = "1", Name = "Sales" },
                new() { Id = "2", Name = "Warehouse" }
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetDepartmentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var result = await _controller.GetDepartments(CancellationToken.None);

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<GetDepartmentsResponse>().Subject;
        payload.Departments.Should().HaveCount(2);
        payload.Departments[0].Name.Should().Be("Sales");

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<GetDepartmentsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetDepartments_ReturnsHandlerFailure_ThroughHandleResponse()
    {
        // Arrange
        var failed = new GetDepartmentsResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InternalServerError,
            Departments = new List<DepartmentDto>()
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetDepartmentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failed);

        // Act
        var result = await _controller.GetDepartments(CancellationToken.None);

        // Assert — InternalServerError maps to 500 via BaseApiController.HandleResponse
        var status = result.Result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GetDepartments_DelegatesToMediator_WithoutAnyControllerSideLogic()
    {
        // Arrange
        var handlerResponse = new GetDepartmentsResponse { Success = true };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetDepartmentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        await _controller.GetDepartments(CancellationToken.None);

        // Assert
        _mediatorMock.Verify(
            m => m.Send(It.IsAny<GetDepartmentsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mediatorMock.VerifyNoOtherCalls();
    }
}
