using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJobsList;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobStatus;
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

public class RecurringJobsControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly RecurringJobsController _controller;

    public RecurringJobsControllerTests()
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
    public async Task GetRecurringJobs_ShouldReturnOkWithJobsList()
    {
        // Arrange
        var expectedJobs = new List<RecurringJobDto>
        {
            new RecurringJobDto
            {
                JobName = "job1",
                DisplayName = "Job 1",
                Description = "Description 1",
                CronExpression = "0 0 * * *",
                IsEnabled = true,
                LastModifiedAt = DateTime.UtcNow,
                LastModifiedBy = "admin"
            },
            new RecurringJobDto
            {
                JobName = "job2",
                DisplayName = "Job 2",
                Description = "Description 2",
                CronExpression = "0 */6 * * *",
                IsEnabled = false,
                LastModifiedAt = DateTime.UtcNow,
                LastModifiedBy = "system"
            }
        };

        var response = new GetRecurringJobsListResponse
        {
            Jobs = expectedJobs,
            Success = true
        };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetRecurringJobsListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetRecurringJobs();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedResponse = okResult.Value.Should().BeAssignableTo<GetRecurringJobsListResponse>().Subject;
        returnedResponse.Success.Should().BeTrue();
        returnedResponse.Jobs.Should().HaveCount(2);
        returnedResponse.Jobs.Should().ContainEquivalentOf(expectedJobs[0]);
        returnedResponse.Jobs.Should().ContainEquivalentOf(expectedJobs[1]);

        _mediatorMock.Verify(x => x.Send(
            It.IsAny<GetRecurringJobsListRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateJobStatus_WithValidJobName_ShouldReturnOkWithUpdatedJob()
    {
        // Arrange
        var jobName = "test-job";
        var requestBody = new UpdateJobStatusRequestBody { IsEnabled = true };
        var expectedResponse = new UpdateRecurringJobStatusResponse
        {
            JobName = jobName,
            IsEnabled = true,
            LastModifiedAt = DateTime.UtcNow,
            LastModifiedBy = "test-user",
            Success = true
        };

        _mediatorMock
            .Setup(x => x.Send(
                It.Is<UpdateRecurringJobStatusRequest>(r => r.JobName == jobName && r.IsEnabled == true),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.UpdateJobStatus(jobName, requestBody);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedResponse = okResult.Value.Should().BeAssignableTo<UpdateRecurringJobStatusResponse>().Subject;
        returnedResponse.Success.Should().BeTrue();
        returnedResponse.JobName.Should().Be(jobName);
        returnedResponse.IsEnabled.Should().BeTrue();

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateRecurringJobStatusRequest>(r =>
                r.JobName == jobName &&
                r.IsEnabled == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateJobStatus_WithInvalidJobName_ShouldReturnNotFound()
    {
        // Arrange
        var jobName = "non-existent-job";
        var requestBody = new UpdateJobStatusRequestBody { IsEnabled = true };
        var errorResponse = new UpdateRecurringJobStatusResponse(
            ErrorCodes.RecurringJobNotFound,
            new Dictionary<string, string> { { "JobName", jobName } });

        _mediatorMock
            .Setup(x => x.Send(
                It.Is<UpdateRecurringJobStatusRequest>(r => r.JobName == jobName),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResponse);

        // Act
        var result = await _controller.UpdateJobStatus(jobName, requestBody);

        // Assert
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var returnedResponse = notFoundResult.Value.Should().BeAssignableTo<UpdateRecurringJobStatusResponse>().Subject;
        returnedResponse.Success.Should().BeFalse();
        returnedResponse.ErrorCode.Should().Be(ErrorCodes.RecurringJobNotFound);

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateRecurringJobStatusRequest>(r => r.JobName == jobName),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateJobStatus_WithDisableRequest_ShouldReturnOkWithDisabledJob()
    {
        // Arrange
        var jobName = "test-job";
        var requestBody = new UpdateJobStatusRequestBody { IsEnabled = false };
        var expectedResponse = new UpdateRecurringJobStatusResponse
        {
            JobName = jobName,
            IsEnabled = false,
            LastModifiedAt = DateTime.UtcNow,
            LastModifiedBy = "test-user",
            Success = true
        };

        _mediatorMock
            .Setup(x => x.Send(
                It.Is<UpdateRecurringJobStatusRequest>(r => r.JobName == jobName && r.IsEnabled == false),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.UpdateJobStatus(jobName, requestBody);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedResponse = okResult.Value.Should().BeAssignableTo<UpdateRecurringJobStatusResponse>().Subject;
        returnedResponse.Success.Should().BeTrue();
        returnedResponse.JobName.Should().Be(jobName);
        returnedResponse.IsEnabled.Should().BeFalse();

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateRecurringJobStatusRequest>(r =>
                r.JobName == jobName &&
                r.IsEnabled == false),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateJobStatus_WhenUpdateFails_ShouldReturnInternalServerError()
    {
        // Arrange
        var jobName = "test-job";
        var requestBody = new UpdateJobStatusRequestBody { IsEnabled = true };
        var errorResponse = new UpdateRecurringJobStatusResponse(
            ErrorCodes.RecurringJobUpdateFailed,
            new Dictionary<string, string>
            {
                { "JobName", jobName },
                { "Message", "Database error" }
            });

        _mediatorMock
            .Setup(x => x.Send(
                It.Is<UpdateRecurringJobStatusRequest>(r => r.JobName == jobName),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResponse);

        // Act
        var result = await _controller.UpdateJobStatus(jobName, requestBody);

        // Assert
        var statusCodeResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
        var returnedResponse = statusCodeResult.Value.Should().BeAssignableTo<UpdateRecurringJobStatusResponse>().Subject;
        returnedResponse.Success.Should().BeFalse();
        returnedResponse.ErrorCode.Should().Be(ErrorCodes.RecurringJobUpdateFailed);

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateRecurringJobStatusRequest>(r => r.JobName == jobName),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
