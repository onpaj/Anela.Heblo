using Anela.Heblo.Application.Features.Attendance;
using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Application.Features.Attendance.UseCases.RunBreakInsertion;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Application.Features.Attendance.Infrastructure.Jobs;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Attendance;

public class RunBreakInsertionValidatorTests
{
    private readonly RunBreakInsertionValidator _validator = new();

    [Fact]
    public void Accepts_ARequestWithoutAWindow_SoTheConfiguredDefaultApplies()
    {
        _validator.Validate(new RunBreakInsertionRequest()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(13, null)]
    [InlineData(40, 30)]
    [InlineData(120, 110)]
    [InlineData(7, 7)]
    public void Accepts_AWindowInsideTheAllowedSpan(int fromDaysAgo, int? toDaysAgo)
    {
        // The span is capped, not the start — a sweep reaches older history by moving the whole
        // window back, which is the only way to cover days the nightly job never sees.
        var request = new RunBreakInsertionRequest { FromDaysAgo = fromDaysAgo, ToDaysAgo = toDaysAgo };

        _validator.Validate(request).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(14, null)]
    [InlineData(40, 20)]
    [InlineData(365, 0)]
    public void Rejects_AWindowWiderThanTheCap(int fromDaysAgo, int? toDaysAgo)
    {
        // The cap keeps a single synchronous call inside the request timeout.
        var request = new RunBreakInsertionRequest { FromDaysAgo = fromDaysAgo, ToDaysAgo = toDaysAgo };

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_AWindowThatStartsAfterItEnds()
    {
        var request = new RunBreakInsertionRequest { FromDaysAgo = 3, ToDaysAgo = 10 };

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, -1)]
    public void Rejects_ANegativeBound(int? fromDaysAgo, int? toDaysAgo)
    {
        var request = new RunBreakInsertionRequest { FromDaysAgo = fromDaysAgo, ToDaysAgo = toDaysAgo };

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ReportsTheFailureInCzech_SoTheApiSurfaceMatchesTheRestOfTheProduct()
    {
        var request = new RunBreakInsertionRequest { FromDaysAgo = 365 };

        var result = _validator.Validate(request);

        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Okno smí pokrývat nejvýše"));
    }
}

public class RunBreakInsertionHandlerTests
{
    private static readonly Guid WorkActivity = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BreakActivity = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Worker = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<ILogetoClient> _client = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    private RunBreakInsertionHandler CreateHandler(
        DateOnly? startDate = null, bool jobEnabled = true, IBreakInsertionRunGate? runGate = null)
    {
        _statusChecker.Setup(c => c.IsJobEnabledAsync(
                BreakInsertionJob.Name, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(jobEnabled);

        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work },
                new() { Guid = BreakActivity, Name = "Oběd", Type = LogetoActivityTypes.Break }
            });
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson> { new() { Guid = Worker, Note = "integration" } });
        _client.Setup(c => c.GetTimeTrackingAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoTimeEntry>());

        var options = new BreakInsertionOptions
        {
            StartDate = startDate ?? new DateOnly(2026, 8, 1),
            BreakActivityName = "Oběd",
            ApiTimesAreUtc = false
        };

        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow())
            .Returns(new DateTimeOffset(2026, 8, 4, 6, 0, 0, TimeSpan.Zero));

        var service = new BreakInsertionService(
            _client.Object, Options.Create(options), timeProvider.Object,
            runGate ?? new BreakInsertionRunGate(), NullLogger<BreakInsertionService>.Instance);

        return new RunBreakInsertionHandler(
            service, _statusChecker.Object, NullLogger<RunBreakInsertionHandler>.Instance);
    }

    [Fact]
    public async Task WidensTheWindow_WhenTheRequestAsksForMoreDays()
    {
        // Arrange — StartDate far back so the request governs the window.
        var handler = CreateHandler(startDate: new DateOnly(2026, 1, 1));

        // Act
        await handler.Handle(
            new RunBreakInsertionRequest { FromDaysAgo = 10 }, CancellationToken.None);

        // Assert — "today" is 2026-08-04, so 10 days back is 2026-07-25.
        _client.Verify(c => c.GetTimeTrackingAsync(
            new DateOnly(2026, 7, 25), new DateOnly(2026, 8, 4), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReachesHistoryTheNightlyWindowNeverSees_WhenTheWindowIsMovedBack()
    {
        // Arrange — the whole point of the explicit window: a bounded span, far from today.
        var handler = CreateHandler(startDate: new DateOnly(2026, 1, 1));

        // Act
        await handler.Handle(
            new RunBreakInsertionRequest { FromDaysAgo = 120, ToDaysAgo = 110 }, CancellationToken.None);

        // Assert — 2026-08-04 minus 120 and 110 days.
        _client.Verify(c => c.GetTimeTrackingAsync(
            new DateOnly(2026, 4, 6), new DateOnly(2026, 4, 16), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefusesToRun_WhenTheJobIsDisabled()
    {
        // Arrange — being disabled is how writes to the live account get stopped.
        var handler = CreateHandler(jobEnabled: false);

        // Act
        var response = await handler.Handle(new RunBreakInsertionRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.RecurringJobDisabled);
        _client.Verify(c => c.GetTimeTrackingAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReportsAConfigurationError_WhenTheBreakActivityIsMissingFromLogeto()
    {
        // Arrange — no Break-typed activity called "Oběd".
        var handler = CreateHandler();
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work }
            });

        // Act
        var response = await handler.Handle(new RunBreakInsertionRequest(), CancellationToken.None);

        // Assert — a raw 500 would leave the UI with nothing to show.
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ConfigurationError);
    }

    [Fact]
    public async Task ReportsAnExternalServiceError_WhenLogetoFails()
    {
        // Arrange
        var handler = CreateHandler();
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("401 Unauthorized"));

        // Act
        var response = await handler.Handle(new RunBreakInsertionRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ExternalServiceError);
    }

    [Fact]
    public async Task FallsBackToTheConfiguredLookback_WhenTheRequestOmitsIt()
    {
        // Arrange
        var handler = CreateHandler(startDate: new DateOnly(2026, 1, 1));

        // Act
        await handler.Handle(new RunBreakInsertionRequest(), CancellationToken.None);

        // Assert — the nightly default of 7 days, ending today.
        _client.Verify(c => c.GetTimeTrackingAsync(
            new DateOnly(2026, 7, 28), new DateOnly(2026, 8, 4), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReturnsTheRunSummary_SoAnAdHocSweepCanBeRead()
    {
        // Arrange — one 8h day that needs a break inserted.
        var handler = CreateHandler();
        var day = new DateOnly(2026, 8, 3);
        _client.Setup(c => c.GetTimeTrackingAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoTimeEntry>
            {
                new()
                {
                    Guid = Guid.NewGuid(), Person = Worker, Date = day, Activity = WorkActivity,
                    From = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
                    To = new DateTimeOffset(2026, 8, 3, 16, 30, 0, TimeSpan.Zero)
                }
            });

        // Act
        var response = await handler.Handle(new RunBreakInsertionRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.BreaksInserted.Should().Be(1);
        response.DaysScanned.Should().Be(1);
    }

    [Fact]
    public async Task RefusesToRun_WhenAnotherWalkIsAlreadyInFlight()
    {
        // Arrange — the nightly job holds the gate while an operator triggers a manual sweep.
        var gate = new BreakInsertionRunGate();
        gate.TryEnter().Should().BeTrue();
        var handler = CreateHandler(runGate: gate);

        // Act
        var response = await handler.Handle(new RunBreakInsertionRequest(), CancellationToken.None);

        // Assert — two overlapping walks would each insert their own break into the same day.
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.RecurringJobAlreadyRunning);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReleasesTheGate_SoALaterRunIsNotBlockedByAnEarlierOne()
    {
        // Arrange
        var gate = new BreakInsertionRunGate();
        var handler = CreateHandler(runGate: gate);

        // Act
        await handler.Handle(new RunBreakInsertionRequest(), CancellationToken.None);
        var second = await handler.Handle(new RunBreakInsertionRequest(), CancellationToken.None);

        // Assert
        second.Success.Should().BeTrue();
    }
}
