using Anela.Heblo.Application.Features.Attendance;
using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Application.Features.Attendance.UseCases.RunBreakInsertion;
using Anela.Heblo.Domain.Features.Attendance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Attendance;

public class RunBreakInsertionValidatorTests
{
    private readonly RunBreakInsertionValidator _validator = new();

    [Fact]
    public void Accepts_ARequestWithoutALookback_SoTheConfiguredDefaultApplies()
    {
        _validator.Validate(new RunBreakInsertionRequest()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    public void Accepts_ALookbackInsideTheAllowedRange(int days)
    {
        _validator.Validate(new RunBreakInsertionRequest { LookbackDays = days }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(32)]
    [InlineData(365)]
    public void Rejects_ALookbackOutsideTheAllowedRange(int days)
    {
        // The cap keeps a single synchronous call inside the request timeout; longer
        // sweeps are run as several calls.
        _validator.Validate(new RunBreakInsertionRequest { LookbackDays = days }).IsValid.Should().BeFalse();
    }
}

public class RunBreakInsertionHandlerTests
{
    private static readonly Guid WorkActivity = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BreakActivity = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Worker = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<ILogetoClient> _client = new();

    private RunBreakInsertionHandler CreateHandler(DateOnly? startDate = null)
    {
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
            NullLogger<BreakInsertionService>.Instance);

        return new RunBreakInsertionHandler(service, NullLogger<RunBreakInsertionHandler>.Instance);
    }

    [Fact]
    public async Task WidensTheWindow_WhenTheRequestAsksForMoreDays()
    {
        // Arrange — StartDate far back so the lookback governs the window.
        var handler = CreateHandler(startDate: new DateOnly(2026, 1, 1));

        // Act
        await handler.Handle(new RunBreakInsertionRequest { LookbackDays = 30 }, CancellationToken.None);

        // Assert — "today" is 2026-08-04, so 30 days back is 2026-07-05.
        _client.Verify(c => c.GetTimeTrackingAsync(
            new DateOnly(2026, 7, 5), new DateOnly(2026, 8, 4), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FallsBackToTheConfiguredLookback_WhenTheRequestOmitsIt()
    {
        // Arrange
        var handler = CreateHandler(startDate: new DateOnly(2026, 1, 1));

        // Act
        await handler.Handle(new RunBreakInsertionRequest(), CancellationToken.None);

        // Assert — the nightly default of 7 days.
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
}
