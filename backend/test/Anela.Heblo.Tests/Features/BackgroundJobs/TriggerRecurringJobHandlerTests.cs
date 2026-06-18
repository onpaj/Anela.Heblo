using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

/// <summary>
/// Pure unit tests for TriggerRecurringJobHandler failure branches.
/// Happy-path coverage lives in TriggerRecurringJobHandlerIntegrationTests.
/// </summary>
public class TriggerRecurringJobHandlerTests
{
    private const string DefaultCronExpression = "0 0 * * *";

    private static TriggerRecurringJobHandler CreateHandler(
        IEnumerable<IRecurringJob>? jobs = null,
        Mock<IRecurringJobStatusChecker>? statusChecker = null,
        Mock<IHangfireJobEnqueuer>? enqueuer = null)
    {
        return new TriggerRecurringJobHandler(
            jobs ?? Array.Empty<IRecurringJob>(),
            (statusChecker ?? new Mock<IRecurringJobStatusChecker>()).Object,
            (enqueuer ?? new Mock<IHangfireJobEnqueuer>()).Object,
            new Mock<ILogger<TriggerRecurringJobHandler>>().Object);
    }

    private static IRecurringJob CreateJob(string jobName)
    {
        var job = new Mock<IRecurringJob>();
        job.SetupGet(j => j.Metadata).Returns(new RecurringJobMetadata
        {
            JobName = jobName,
            DisplayName = jobName,
            Description = "test",
            CronExpression = DefaultCronExpression
        });
        return job.Object;
    }

    [Fact]
    public async Task Handle_WhenJobIsNotRegistered_ReturnsRecurringJobNotFound()
    {
        // Arrange
        var handler = CreateHandler(jobs: Array.Empty<IRecurringJob>());
        var request = new TriggerRecurringJobRequest { JobName = "missing-job", ForceDisabled = false };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.RecurringJobNotFound);
        response.Params.Should().ContainKey("jobName").WhoseValue.Should().Be("missing-job");
    }

    [Fact]
    public async Task Handle_WhenJobIsDisabledAndForceDisabledIsFalse_ReturnsRecurringJobDisabled()
    {
        // Arrange
        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(x => x.IsJobEnabledAsync("disabled-job", It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(false);

        var handler = CreateHandler(
            jobs: new[] { CreateJob("disabled-job") },
            statusChecker: statusChecker);

        var request = new TriggerRecurringJobRequest { JobName = "disabled-job", ForceDisabled = false };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.RecurringJobDisabled);
        response.Params.Should().ContainKey("jobName").WhoseValue.Should().Be("disabled-job");
    }

    [Fact]
    public async Task Handle_WhenEnqueuerReturnsNull_ReturnsRecurringJobEnqueueFailed()
    {
        // Arrange
        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(x => x.IsJobEnabledAsync("enabled-job", It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(true);

        var enqueuer = new Mock<IHangfireJobEnqueuer>();
        enqueuer
            .Setup(x => x.EnqueueJob(It.IsAny<IRecurringJob>(), It.IsAny<CancellationToken>()))
            .Returns((string?)null);

        var handler = CreateHandler(
            jobs: new[] { CreateJob("enabled-job") },
            statusChecker: statusChecker,
            enqueuer: enqueuer);

        var request = new TriggerRecurringJobRequest { JobName = "enabled-job", ForceDisabled = false };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.RecurringJobEnqueueFailed);
        response.Params.Should().ContainKey("jobName").WhoseValue.Should().Be("enabled-job");
    }
}
