using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Attendance.UseCases.RunBreakInsertion;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class AttendanceControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly AttendanceController _controller;

    public AttendanceControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new AttendanceController(_mediatorMock.Object);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        var serviceProvider = serviceCollection.BuildServiceProvider();

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = serviceProvider }
        };
    }

    [Fact]
    public async Task RunBreakInsertion_PassesTheRequestedWindowThrough()
    {
        // Arrange
        var request = new RunBreakInsertionRequest { FromDaysAgo = 120, ToDaysAgo = 110 };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RunBreakInsertionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunBreakInsertionResponse { DaysScanned = 11, RecordsTouched = 4 });

        // Act
        var result = await _controller.RunBreakInsertion(request, CancellationToken.None);

        // Assert
        var response = result.Result.Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should().BeOfType<RunBreakInsertionResponse>().Subject;
        response.DaysScanned.Should().Be(11);
        response.RecordsTouched.Should().Be(4);

        _mediatorMock.Verify(m => m.Send(
            It.Is<RunBreakInsertionRequest>(r => r.FromDaysAgo == 120 && r.ToDaysAgo == 110),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunBreakInsertion_SurfacesTheRefusal_WhenTheJobIsDisabled()
    {
        // Arrange — the off-switch must reach the caller as a 409, not a 200 with empty counters.
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RunBreakInsertionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunBreakInsertionResponse(ErrorCodes.RecurringJobDisabled));

        // Act
        var result = await _controller.RunBreakInsertion(
            new RunBreakInsertionRequest(), CancellationToken.None);

        // Assert
        var objectResult = result.Result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void RunBreakInsertion_IsGatedByTheJobTriggerPermission()
    {
        // The endpoint writes to a live external account; it must not be reachable without the
        // same permission the generic job trigger requires.
        var attribute = typeof(AttendanceController)
            .GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Feature.Should().Be(Feature.Jobs_Trigger);
    }
}
