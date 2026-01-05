using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

/// <summary>
/// Tests for RecurringJobsController.TriggerJob endpoint
/// </summary>
public class RecurringJobsControllerTriggerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly RecurringJobsController _controller;

    public RecurringJobsControllerTriggerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new RecurringJobsController(_mediatorMock.Object);

        // Setup mock user context
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-123")
        }));

        // Setup service provider for BaseApiController logging
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        var loggerMock = new Mock<ILogger<RecurringJobsController>>();

        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(loggerMock.Object);

        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory)))
            .Returns(loggerFactoryMock.Object);

        var httpContext = new DefaultHttpContext
        {
            User = user,
            RequestServices = serviceProviderMock.Object
        };

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public async Task TriggerJob_WithValidJobName_ShouldReturnAcceptedWithSuccessResponse()
    {
        // Arrange
        var jobName = "test-job";
        var expectedResponse = new TriggerRecurringJobResponse
        {
            JobId = "background-job-id-123",
            Success = true
        };

        _mediatorMock
            .Setup(x => x.Send(
                It.Is<TriggerRecurringJobRequest>(r => r.JobName == jobName),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.TriggerJob(jobName);

        // Assert
        var acceptedResult = result.Result.Should().BeOfType<AcceptedResult>().Subject;
        var returnedResponse = acceptedResult.Value.Should().BeAssignableTo<TriggerRecurringJobResponse>().Subject;
        returnedResponse.Success.Should().BeTrue();
        returnedResponse.JobId.Should().Be("background-job-id-123");

        _mediatorMock.Verify(x => x.Send(
            It.Is<TriggerRecurringJobRequest>(r => r.JobName == jobName),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TriggerJob_WithNonExistentJobName_ShouldReturnNotFound()
    {
        // Arrange
        var jobName = "non-existent-job";
        var errorResponse = new TriggerRecurringJobResponse
        {
            Success = false,
            ErrorMessage = $"Job '{jobName}' not found or is disabled (use forceDisabled to override)"
        };

        _mediatorMock
            .Setup(x => x.Send(
                It.Is<TriggerRecurringJobRequest>(r => r.JobName == jobName),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResponse);

        // Act
        var result = await _controller.TriggerJob(jobName);

        // Assert
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var returnedResponse = notFoundResult.Value.Should().BeAssignableTo<TriggerRecurringJobResponse>().Subject;
        returnedResponse.Success.Should().BeFalse();
        returnedResponse.ErrorMessage.Should().Contain(jobName);

        _mediatorMock.Verify(x => x.Send(
            It.Is<TriggerRecurringJobRequest>(r => r.JobName == jobName),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TriggerJob_WhenTriggerFails_ShouldReturnBadRequest()
    {
        // Arrange
        var jobName = "test-job";
        var errorResponse = new TriggerRecurringJobResponse
        {
            Success = false,
            ErrorMessage = "Hangfire service error"
        };

        _mediatorMock
            .Setup(x => x.Send(
                It.Is<TriggerRecurringJobRequest>(r => r.JobName == jobName),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResponse);

        // Act
        var result = await _controller.TriggerJob(jobName);

        // Assert
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var returnedResponse = notFoundResult.Value.Should().BeAssignableTo<TriggerRecurringJobResponse>().Subject;
        returnedResponse.Success.Should().BeFalse();
        returnedResponse.ErrorMessage.Should().Be("Hangfire service error");

        _mediatorMock.Verify(x => x.Send(
            It.Is<TriggerRecurringJobRequest>(r => r.JobName == jobName),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TriggerJob_ShouldPassCancellationTokenToMediator()
    {
        // Arrange
        var jobName = "test-job";
        var cancellationToken = new CancellationToken();
        var expectedResponse = new TriggerRecurringJobResponse
        {
            JobId = "background-job-id-123",
            Success = true
        };

        _mediatorMock
            .Setup(x => x.Send(
                It.IsAny<TriggerRecurringJobRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        await _controller.TriggerJob(jobName, cancellationToken);

        // Assert
        _mediatorMock.Verify(x => x.Send(
            It.Is<TriggerRecurringJobRequest>(r => r.JobName == jobName),
            cancellationToken), Times.Once);
    }
}
