using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetRunningJobsForGiftPackage;
using Anela.Heblo.Xcc.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics;

public class GetRunningJobsForGiftPackageHandlerTests
{
    private readonly Mock<IBackgroundWorker> _backgroundWorkerMock;
    private readonly GetRunningJobsForGiftPackageHandler _handler;

    public GetRunningJobsForGiftPackageHandlerTests()
    {
        _backgroundWorkerMock = new Mock<IBackgroundWorker>();
        _handler = new GetRunningJobsForGiftPackageHandler(_backgroundWorkerMock.Object);
    }

    [Fact]
    public async Task Handle_WithRunningAndPendingGiftPackageJobs_ReturnsFilteredJobs()
    {
        // Arrange
        var request = new GetRunningJobsForGiftPackageRequest { GiftPackageCode = "GIFT001" };

        var runningJobs = new List<BackgroundJobInfo>
        {
            new()
            {
                Id = "running-1",
                JobName = "GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5, true, <CancellationToken>)",
                State = "Processing",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                StartedAt = DateTime.UtcNow.AddMinutes(-2),
                Queue = "gift-packages"
            },
            new()
            {
                Id = "running-2",
                JobName = "OtherService.DoSomething()",
                State = "Processing",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3),
                StartedAt = DateTime.UtcNow.AddMinutes(-1),
                Queue = "other-queue"
            }
        };

        var pendingJobs = new List<BackgroundJobInfo>
        {
            new()
            {
                Id = "pending-1",
                JobName = "GiftPackageManufactureService.CreateManufactureAsync(\"GIFT002\", 3, false, <CancellationToken>)",
                State = "Enqueued",
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                Queue = "gift-packages"
            }
        };

        _backgroundWorkerMock.Setup(x => x.GetRunningJobs()).Returns(runningJobs);
        _backgroundWorkerMock.Setup(x => x.GetPendingJobs()).Returns(pendingJobs);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.RunningJobs.Should().HaveCount(1); // Only GIFT001 job should be returned
        result.RunningJobs[0].JobId.Should().Be("running-1");
        result.RunningJobs[0].DisplayName.Should().Contain("GiftPackageManufactureService.CreateManufactureAsync");
        result.RunningJobs[0].DisplayName.Should().Contain("GIFT001");
    }

    [Fact]
    public async Task Handle_WithoutGiftPackageCodeFilter_ReturnsAllGiftPackageJobs()
    {
        // Arrange
        var request = new GetRunningJobsForGiftPackageRequest(); // No specific gift package code

        var runningJobs = new List<BackgroundJobInfo>
        {
            new()
            {
                Id = "running-1",
                JobName = "GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5, true, <CancellationToken>)",
                State = "Processing",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                StartedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new()
            {
                Id = "running-2",
                JobName = "GiftPackageManufactureService.CreateManufactureAsync(\"GIFT002\", 3, false, <CancellationToken>)",
                State = "Processing",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3),
                StartedAt = DateTime.UtcNow.AddMinutes(-1)
            },
            new()
            {
                Id = "running-3",
                JobName = "OtherService.DoSomething()",
                State = "Processing",
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            }
        };

        _backgroundWorkerMock.Setup(x => x.GetRunningJobs()).Returns(runningJobs);
        _backgroundWorkerMock.Setup(x => x.GetPendingJobs()).Returns(new List<BackgroundJobInfo>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.RunningJobs.Should().HaveCount(2); // Both GIFT001 and GIFT002 jobs
        result.RunningJobs.Should().OnlyContain(job => job.DisplayName!.Contains("GiftPackageManufactureService"));
    }

    [Fact]
    public async Task Handle_WithNoGiftPackageJobs_ReturnsEmptyList()
    {
        // Arrange
        var request = new GetRunningJobsForGiftPackageRequest();

        var runningJobs = new List<BackgroundJobInfo>
        {
            new()
            {
                Id = "other-1",
                JobName = "OtherService.DoSomething()",
                State = "Processing"
            }
        };

        _backgroundWorkerMock.Setup(x => x.GetRunningJobs()).Returns(runningJobs);
        _backgroundWorkerMock.Setup(x => x.GetPendingJobs()).Returns(new List<BackgroundJobInfo>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.RunningJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenGetRunningJobsThrows_ReturnsEmptyListGracefully()
    {
        // Arrange
        var request = new GetRunningJobsForGiftPackageRequest();

        _backgroundWorkerMock.Setup(x => x.GetRunningJobs())
            .Throws(new InvalidOperationException("Hangfire monitoring unavailable"));
        _backgroundWorkerMock.Setup(x => x.GetPendingJobs())
            .Returns(new List<BackgroundJobInfo>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.RunningJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenGetPendingJobsThrows_ReturnsEmptyListGracefully()
    {
        // Arrange
        var request = new GetRunningJobsForGiftPackageRequest();

        _backgroundWorkerMock.Setup(x => x.GetRunningJobs())
            .Returns(new List<BackgroundJobInfo>());
        _backgroundWorkerMock.Setup(x => x.GetPendingJobs())
            .Throws(new InvalidOperationException("Hangfire monitoring unavailable"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.RunningJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenBothGetMethodsThrow_ReturnsEmptyListGracefully()
    {
        // Arrange
        var request = new GetRunningJobsForGiftPackageRequest();

        _backgroundWorkerMock.Setup(x => x.GetRunningJobs())
            .Throws(new InvalidOperationException("Hangfire monitoring unavailable"));
        _backgroundWorkerMock.Setup(x => x.GetPendingJobs())
            .Throws(new InvalidOperationException("Hangfire monitoring unavailable"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.RunningJobs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("GiftPackageManufactureService.CreateManufactureAsync", true)]
    [InlineData("GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5)", true)]
    [InlineData("OtherService.CreateManufactureAsync", false)]
    [InlineData("GiftPackageService.DoSomething", false)]
    [InlineData("SomeService.GiftPackageManufactureService", false)] // Should not match partial name
    [InlineData("", false)]
    public async Task Handle_JobFilteringLogic_WorksCorrectly(string jobName, bool shouldBeIncluded)
    {
        // Arrange
        var request = new GetRunningJobsForGiftPackageRequest();

        var jobs = new List<BackgroundJobInfo>();
        if (!string.IsNullOrEmpty(jobName))
        {
            jobs.Add(new BackgroundJobInfo
            {
                Id = "test-job",
                JobName = jobName,
                State = "Processing"
            });
        }

        _backgroundWorkerMock.Setup(x => x.GetRunningJobs()).Returns(jobs);
        _backgroundWorkerMock.Setup(x => x.GetPendingJobs()).Returns(new List<BackgroundJobInfo>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        if (shouldBeIncluded)
        {
            result.RunningJobs.Should().HaveCount(1);
            result.RunningJobs[0].JobId.Should().Be("test-job");
        }
        else
        {
            result.RunningJobs.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Handle_WithCaseInsensitiveGiftPackageCodeFilter_WorksCorrectly()
    {
        // Arrange
        var request = new GetRunningJobsForGiftPackageRequest { GiftPackageCode = "gift001" }; // lowercase

        var runningJobs = new List<BackgroundJobInfo>
        {
            new()
            {
                Id = "running-1",
                JobName = "GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5)", // uppercase in job name
                State = "Processing"
            }
        };

        _backgroundWorkerMock.Setup(x => x.GetRunningJobs()).Returns(runningJobs);
        _backgroundWorkerMock.Setup(x => x.GetPendingJobs()).Returns(new List<BackgroundJobInfo>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.RunningJobs.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_MapsJobDataCorrectly()
    {
        // Arrange
        var request = new GetRunningJobsForGiftPackageRequest();
        var createdAt = DateTime.UtcNow.AddMinutes(-10);
        var startedAt = DateTime.UtcNow.AddMinutes(-5);

        var runningJobs = new List<BackgroundJobInfo>
        {
            new()
            {
                Id = "test-job-id",
                JobName = "GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5)",
                State = "Processing",
                CreatedAt = createdAt,
                StartedAt = startedAt,
                Queue = "gift-packages"
            }
        };

        _backgroundWorkerMock.Setup(x => x.GetRunningJobs()).Returns(runningJobs);
        _backgroundWorkerMock.Setup(x => x.GetPendingJobs()).Returns(new List<BackgroundJobInfo>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.RunningJobs.Should().HaveCount(1);

        var jobDto = result.RunningJobs[0];
        jobDto.JobId.Should().Be("test-job-id");
        jobDto.Status.Should().Be("Processing");
        jobDto.DisplayName.Should().Be("GiftPackageManufactureService.CreateManufactureAsync(\"GIFT001\", 5)");
        jobDto.CreatedAt.Should().Be(createdAt);
        jobDto.StartedAt.Should().Be(startedAt);
    }
}