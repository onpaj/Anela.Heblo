using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageManufactureJobStatus;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.Logistics.GiftPackageManufacture;

public class GetGiftPackageManufactureJobStatusHandlerTests
{
    [Fact]
    public async Task Handle_JobNotFound_ReturnsFailureResponse()
    {
        // Arrange
        var handler = new GetGiftPackageManufactureJobStatusHandler();
        var request = new GetGiftPackageManufactureJobStatusRequest 
        { 
            JobId = "non-existent-job" 
        };

        // Mock JobStorage to return null job data
        var mockConnection = new Mock<IStorageConnection>();
        mockConnection.Setup(x => x.GetJobData("non-existent-job"))
                     .Returns((JobData?)null);

        var mockStorage = new Mock<JobStorage>();
        mockStorage.Setup(x => x.GetConnection())
                  .Returns(mockConnection.Object);

        JobStorage.Current = mockStorage.Object;

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_ValidJobId_ReturnsJobStatus()
    {
        // Arrange
        var handler = new GetGiftPackageManufactureJobStatusHandler();
        var request = new GetGiftPackageManufactureJobStatusRequest 
        { 
            JobId = "test-job-123" 
        };

        var mockConnection = new Mock<IStorageConnection>();
        var mockJobData = new JobData
        {
            State = "Succeeded",
            Job = null,
            LoadException = null
        };

        mockConnection.Setup(x => x.GetJobData("test-job-123"))
                     .Returns(mockJobData);

        var stateHistory = new List<StateHistoryDto>
        {
            new() { Name = "Enqueued", CreatedAt = DateTime.UtcNow.AddMinutes(-5) },
            new() { Name = "Processing", CreatedAt = DateTime.UtcNow.AddMinutes(-3) },
            new() { Name = "Succeeded", CreatedAt = DateTime.UtcNow }
        };

        mockConnection.Setup(x => x.GetStateHistory("test-job-123"))
                     .Returns(stateHistory);

        var mockStorage = new Mock<JobStorage>();
        mockStorage.Setup(x => x.GetConnection())
                  .Returns(mockConnection.Object);

        JobStorage.Current = mockStorage.Object;

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.JobStatus.Should().NotBeNull();
        result.JobStatus.JobId.Should().Be("test-job-123");
        result.JobStatus.Status.Should().Be("Succeeded");
        result.JobStatus.IsRunning.Should().BeFalse();
    }
}