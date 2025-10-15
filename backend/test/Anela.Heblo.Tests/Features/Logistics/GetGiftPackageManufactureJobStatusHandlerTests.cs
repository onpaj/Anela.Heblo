using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageManufactureJobStatus;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Xcc.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics;

public class GetGiftPackageManufactureJobStatusHandlerTests
{
    private readonly Mock<IBackgroundWorker> _backgroundWorkerMock;
    private readonly GetGiftPackageManufactureJobStatusHandler _handler;

    public GetGiftPackageManufactureJobStatusHandlerTests()
    {
        _backgroundWorkerMock = new Mock<IBackgroundWorker>();
        _handler = new GetGiftPackageManufactureJobStatusHandler(_backgroundWorkerMock.Object);
    }

    [Fact]
    public async Task Handle_WithExistingProcessingJob_ReturnsJobStatusWithDetails()
    {
        // Arrange
        var request = new GetGiftPackageManufactureJobStatusRequest { JobId = "existing-job-id" };
        var createdAt = DateTime.UtcNow.AddMinutes(-10);
        var startedAt = DateTime.UtcNow.AddMinutes(-5);

        var jobInfo = new BackgroundJobInfo
        {
            Id = "existing-job-id",
            JobName = "GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5)",
            State = "Processing",
            CreatedAt = createdAt,
            StartedAt = startedAt,
            Queue = "gift-packages"
        };

        _backgroundWorkerMock.Setup(x => x.GetJobById("existing-job-id")).Returns(jobInfo);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.JobStatus.Should().NotBeNull();
        result.JobStatus.JobId.Should().Be("existing-job-id");
        result.JobStatus.Status.Should().Be("Processing");
        result.JobStatus.DisplayName.Should().Be("GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5)");
        result.JobStatus.CreatedAt.Should().Be(createdAt);
        result.JobStatus.StartedAt.Should().Be(startedAt);
        result.JobStatus.CompletedAt.Should().BeNull(); // Processing job should not have completion time
        result.JobStatus.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithSucceededJob_EstimatesCompletionTime()
    {
        // Arrange
        var request = new GetGiftPackageManufactureJobStatusRequest { JobId = "succeeded-job-id" };
        var createdAt = DateTime.UtcNow.AddMinutes(-15);
        var startedAt = DateTime.UtcNow.AddMinutes(-10);

        var jobInfo = new BackgroundJobInfo
        {
            Id = "succeeded-job-id",
            JobName = "GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5)",
            State = "Succeeded",
            CreatedAt = createdAt,
            StartedAt = startedAt,
            Queue = "gift-packages"
        };

        _backgroundWorkerMock.Setup(x => x.GetJobById("succeeded-job-id")).Returns(jobInfo);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.JobStatus.Should().NotBeNull();
        result.JobStatus.JobId.Should().Be("succeeded-job-id");
        result.JobStatus.Status.Should().Be("Succeeded");
        result.JobStatus.CompletedAt.Should().NotBeNull();
        result.JobStatus.CompletedAt.Should().Be(startedAt.AddMinutes(1)); // Expected completion time estimation
        result.JobStatus.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithFailedJob_IncludesErrorMessage()
    {
        // Arrange
        var request = new GetGiftPackageManufactureJobStatusRequest { JobId = "failed-job-id" };
        var createdAt = DateTime.UtcNow.AddMinutes(-15);
        var startedAt = DateTime.UtcNow.AddMinutes(-10);

        var jobInfo = new BackgroundJobInfo
        {
            Id = "failed-job-id",
            JobName = "GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5)",
            State = "Failed",
            CreatedAt = createdAt,
            StartedAt = startedAt,
            Queue = "gift-packages"
        };

        _backgroundWorkerMock.Setup(x => x.GetJobById("failed-job-id")).Returns(jobInfo);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.JobStatus.Should().NotBeNull();
        result.JobStatus.JobId.Should().Be("failed-job-id");
        result.JobStatus.Status.Should().Be("Failed");
        result.JobStatus.CompletedAt.Should().NotBeNull();
        result.JobStatus.ErrorMessage.Should().Be("Job execution failed. Please check logs for details.");
    }

    [Fact]
    public async Task Handle_WithJobWithoutStartedTime_EstimatesCompletionFromCreatedTime()
    {
        // Arrange
        var request = new GetGiftPackageManufactureJobStatusRequest { JobId = "completed-job-no-start" };
        var createdAt = DateTime.UtcNow.AddMinutes(-15);

        var jobInfo = new BackgroundJobInfo
        {
            Id = "completed-job-no-start",
            JobName = "GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5)",
            State = "Succeeded",
            CreatedAt = createdAt,
            StartedAt = null, // No started time
            Queue = "gift-packages"
        };

        _backgroundWorkerMock.Setup(x => x.GetJobById("completed-job-no-start")).Returns(jobInfo);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.JobStatus.CompletedAt.Should().Be(createdAt.AddMinutes(2)); // Fallback estimation
    }

    [Fact]
    public async Task Handle_WithNonExistentJob_ReturnsNotFoundError()
    {
        // Arrange
        var request = new GetGiftPackageManufactureJobStatusRequest { JobId = "non-existent-job" };

        _backgroundWorkerMock.Setup(x => x.GetJobById("non-existent-job")).Returns((BackgroundJobInfo?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        result.Params.Should().ContainKey("JobId");
        result.Params["JobId"].Should().Be("non-existent-job");
        
        result.JobStatus.Should().NotBeNull();
        result.JobStatus.JobId.Should().Be("non-existent-job");
        result.JobStatus.Status.Should().Be("NotFound");
        result.JobStatus.DisplayName.Should().Be("Job not found");
        result.JobStatus.ErrorMessage.Should().Be("Job with ID 'non-existent-job' not found.");
    }

    [Fact]
    public async Task Handle_WhenBackgroundWorkerThrows_ReturnsErrorResponse()
    {
        // Arrange
        var request = new GetGiftPackageManufactureJobStatusRequest { JobId = "error-job" };

        _backgroundWorkerMock.Setup(x => x.GetJobById("error-job"))
            .Throws(new InvalidOperationException("Hangfire monitoring unavailable"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Exception);
        result.Params.Should().ContainKey("ErrorMessage");
        result.Params["ErrorMessage"].Should().Be("Hangfire monitoring unavailable");
        
        result.JobStatus.Should().NotBeNull();
        result.JobStatus.JobId.Should().Be("error-job");
        result.JobStatus.Status.Should().Be("Error");
        result.JobStatus.DisplayName.Should().Be("Error retrieving job");
        result.JobStatus.ErrorMessage.Should().Be("Hangfire monitoring unavailable");
    }

    [Fact]
    public async Task Handle_WithDatabaseError_ReturnsErrorResponse()
    {
        // Arrange
        var request = new GetGiftPackageManufactureJobStatusRequest { JobId = "db-error-job" };

        _backgroundWorkerMock.Setup(x => x.GetJobById("db-error-job"))
            .Throws(new InvalidOperationException("Database connection failed"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Exception);
        result.JobStatus.Status.Should().Be("Error");
    }

    [Theory]
    [InlineData("Enqueued")]
    [InlineData("Scheduled")]
    [InlineData("Processing")]
    [InlineData("Awaiting")]
    public async Task Handle_WithNonCompletedJobStates_DoesNotSetCompletionTime(string jobState)
    {
        // Arrange
        var request = new GetGiftPackageManufactureJobStatusRequest { JobId = "test-job" };

        var jobInfo = new BackgroundJobInfo
        {
            Id = "test-job",
            JobName = "GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5)",
            State = jobState,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            StartedAt = DateTime.UtcNow.AddMinutes(-2),
            Queue = "gift-packages"
        };

        _backgroundWorkerMock.Setup(x => x.GetJobById("test-job")).Returns(jobInfo);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.JobStatus.Status.Should().Be(jobState);
        result.JobStatus.CompletedAt.Should().BeNull();
        result.JobStatus.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithEmptyJobId_CallsBackgroundWorkerCorrectly()
    {
        // Arrange
        var request = new GetGiftPackageManufactureJobStatusRequest { JobId = "" };

        _backgroundWorkerMock.Setup(x => x.GetJobById("")).Returns((BackgroundJobInfo?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        _backgroundWorkerMock.Verify(x => x.GetJobById(""), Times.Once);
        result.Success.Should().BeFalse();
        result.JobStatus.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task Handle_ErrorResponseMapping_FollowsBaseResponsePattern()
    {
        // Arrange
        var request = new GetGiftPackageManufactureJobStatusRequest { JobId = "test-job" };

        _backgroundWorkerMock.Setup(x => x.GetJobById("test-job")).Returns((BackgroundJobInfo?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert - Verify BaseResponse error pattern is followed
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().HaveValue();
        result.Params.Should().NotBeNull();
        result.Params.Should().NotBeEmpty();

        // Verify job status is still provided even in error case
        result.JobStatus.Should().NotBeNull();
        result.JobStatus.JobId.Should().Be("test-job");
    }

    [Fact]
    public async Task Handle_SuccessResponseMapping_FollowsBaseResponsePattern()
    {
        // Arrange
        var request = new GetGiftPackageManufactureJobStatusRequest { JobId = "success-job" };

        var jobInfo = new BackgroundJobInfo
        {
            Id = "success-job",
            JobName = "GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5)",
            State = "Processing",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            Queue = "gift-packages"
        };

        _backgroundWorkerMock.Setup(x => x.GetJobById("success-job")).Returns(jobInfo);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert - Verify BaseResponse success pattern is followed
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Params.Should().BeNull();
        result.JobStatus.Should().NotBeNull();
    }
}