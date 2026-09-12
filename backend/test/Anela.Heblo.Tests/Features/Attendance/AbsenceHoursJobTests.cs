using Anela.Heblo.Application.Features.Attendance;
using Anela.Heblo.Application.Features.Attendance.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Attendance;

public class AbsenceHoursJobTests
{
    // AbsenceHoursService is a concrete class (no interface), so it can't be mocked
    // directly. Instead we build a real service around a mocked ILogetoClient and use
    // client calls as a proxy for "did RunAsync execute": GetActivitiesAsync is the
    // first call RunAsync makes, so verifying it was (not) called proves the job did
    // (not) invoke RunAsync.
    private readonly Mock<ILogetoClient> _client = new();

    private AbsenceHoursJob CreateJob(Mock<IRecurringJobStatusChecker> statusCheckerMock)
    {
        var service = new AbsenceHoursService(
            _client.Object,
            Options.Create(new AbsenceHoursOptions { StartDate = new DateOnly(2026, 8, 1) }),
            TimeProvider.System,
            NullLogger<AbsenceHoursService>.Instance);

        return new AbsenceHoursJob(
            service,
            statusCheckerMock.Object,
            NullLogger<AbsenceHoursJob>.Instance);
    }

    private static Mock<IRecurringJobStatusChecker> StatusChecker(bool enabled)
    {
        var mock = new Mock<IRecurringJobStatusChecker>();
        mock.Setup(s => s.IsJobEnabledAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(enabled);
        return mock;
    }

    private void SetupClientToReturnEarly()
    {
        // An empty People list makes RunAsync return right after the two lookups, with no
        // need to stub GetTimeTrackingAsync/UpdateTimeEntryAsync.
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = Guid.NewGuid(), Name = "Dovolená", Type = LogetoActivityTypes.Absence }
            });

        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>());
    }

    [Fact]
    public void Metadata_DeclaresTheAgreedNameCronAndDisabledDefault()
    {
        // Arrange
        var job = CreateJob(StatusChecker(enabled: false));

        // Act
        var metadata = job.Metadata;

        // Assert
        metadata.JobName.Should().Be("logeto-absence-hours");
        metadata.CronExpression.Should().Be("0 4 * * *");
        metadata.DefaultIsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenJobDisabled_DoesNotInvokeService()
    {
        // Arrange
        var job = CreateJob(StatusChecker(enabled: false));

        // Act
        await job.ExecuteAsync();

        // Assert
        _client.Verify(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenJobEnabled_InvokesServiceExactlyOnce()
    {
        // Arrange
        SetupClientToReturnEarly();
        var job = CreateJob(StatusChecker(enabled: true));

        // Act
        await job.ExecuteAsync();

        // Assert
        _client.Verify(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
