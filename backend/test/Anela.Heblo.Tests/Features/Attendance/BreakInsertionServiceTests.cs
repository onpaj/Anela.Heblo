using Anela.Heblo.Application.Features.Attendance;
using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Domain.Features.Attendance;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Attendance;

public class BreakInsertionServiceTests
{
    private static readonly Guid WorkActivity = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BreakActivity = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Worker = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateOnly Day = new(2026, 8, 3);
    private static readonly DateOnly Today = new(2026, 8, 4); // matches the fixed "now" in CreateService

    private readonly Mock<ILogetoClient> _client = new();

    private BreakInsertionService CreateService(
        BreakInsertionOptions? options = null, ILogger<BreakInsertionService>? logger = null)
    {
        options ??= new BreakInsertionOptions
        {
            StartDate = new DateOnly(2026, 8, 1),
            BreakActivityName = "Oběd",
            ApiTimesAreUtc = false // tests use wall-clock times directly for readability
        };

        // Fixed "now": 2026-08-04 08:00 Prague — so "yesterday" = 2026-08-03.
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow())
            .Returns(new DateTimeOffset(2026, 8, 4, 6, 0, 0, TimeSpan.Zero));

        return new BreakInsertionService(
            _client.Object,
            Options.Create(options),
            timeProvider.Object,
            logger ?? NullLogger<BreakInsertionService>.Instance);
    }

    private void SetupDefaults(params LogetoTimeEntry[] entries)
    {
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work },
                new() { Guid = BreakActivity, Name = "Oběd", Type = LogetoActivityTypes.Break }
            });

        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>
            {
                new() { Guid = Worker, Note = "integration", Inactive = false },
                new() { Guid = Guid.NewGuid(), Note = "somebody else", Inactive = false }
            });

        _client.Setup(c => c.GetTimeTrackingAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries.ToList());
    }

    private static LogetoTimeEntry WorkEntryOn(DateOnly date, int fromHour, int fromMin, int toHour, int toMin) => new()
    {
        Guid = Guid.NewGuid(),
        Person = Worker,
        Date = date,
        Activity = WorkActivity,
        From = new DateTimeOffset(date.Year, date.Month, date.Day, fromHour, fromMin, 0, TimeSpan.Zero),
        To = new DateTimeOffset(date.Year, date.Month, date.Day, toHour, toMin, 0, TimeSpan.Zero)
    };

    private static LogetoTimeEntry WorkEntry(int fromHour, int fromMin, int toHour, int toMin) =>
        WorkEntryOn(Day, fromHour, fromMin, toHour, toMin);

    [Fact]
    public async Task InsertsBreak_ForEightHourDayWithoutBreak()
    {
        SetupDefaults(WorkEntry(8, 0, 16, 30));

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.Is<LogetoCreateTimeEntryRequest>(r =>
                r.Person == Worker
                && r.Activity == BreakActivity
                && r.Date == Day
                && r.From == "2026-08-03T11:00:00"
                && r.To == "2026-08-03T11:30:00"
                && r.Billable == false
                && r.ExternalKey == $"autobreak-{Worker}-2026-08-03"),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestsTimeTracking_ForTheRollingWindow_WhenStartDateIsFarInThePast()
    {
        SetupDefaults();
        var options = new BreakInsertionOptions
        {
            StartDate = new DateOnly(2026, 1, 1), // far past — the lookback governs
            BreakActivityName = "Oběd",
            ApiTimesAreUtc = false
        };

        await CreateService(options).RunAsync(CancellationToken.None);

        // "now" is 2026-08-04; default lookback of 7 days → window starts 2026-07-28, ends today.
        _client.Verify(c => c.GetTimeTrackingAsync(
            new DateOnly(2026, 7, 28), new DateOnly(2026, 8, 4), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClampsWindowStart_ToStartDate_WhenLookbackReachesPastIt()
    {
        SetupDefaults();

        // Default options: StartDate 2026-08-01, lookback 7 → 2026-07-28 clamped up to the floor.
        await CreateService().RunAsync(CancellationToken.None);

        _client.Verify(c => c.GetTimeTrackingAsync(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 4), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoesNothing_AndCallsNoApi_WhenStartDateIsInTheFuture()
    {
        SetupDefaults();
        var options = new BreakInsertionOptions
        {
            StartDate = new DateOnly(2026, 9, 1), // after "now" of 2026-08-04
            BreakActivityName = "Oběd",
            ApiTimesAreUtc = false
        };

        var summary = await CreateService(options).RunAsync(CancellationToken.None);

        summary.DaysScanned.Should().Be(0);
        summary.BreaksInserted.Should().Be(0);
        _client.Verify(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.GetTimeTrackingAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SkipsDay_WhenAnyBreakAlreadyExists()
    {
        var existingBreak = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = BreakActivity,
            From = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 3, 12, 10, 0, TimeSpan.Zero)
        };
        SetupDefaults(WorkEntry(8, 0, 16, 30), existingBreak);

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(0);
        summary.SkippedExistingBreak.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.IsAny<LogetoCreateTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SkipsDay_BelowSixHours()
    {
        SetupDefaults(WorkEntry(8, 0, 13, 30)); // 5.5 h

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(0);
        summary.SkippedBelowThreshold.Should().Be(1);
    }

    [Fact]
    public async Task InsertsBreak_AtExactlySixHours()
    {
        SetupDefaults(WorkEntry(8, 0, 14, 0)); // exactly 6 h — inclusive threshold

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
    }

    [Fact]
    public async Task SkipsDayWithWarning_WhenThresholdOnlyReachedByHoursOnlyRecords()
    {
        var hoursOnly = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = WorkActivity,
            Hours = "05:00:00" // no From/To window
        };
        SetupDefaults(WorkEntry(8, 0, 10, 0), hoursOnly); // 2h windowed + 5h duration-only = 7h total

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(0);
        summary.SkippedHoursOnly.Should().Be(1);
    }

    [Fact]
    public async Task LogsWarning_WhenWorkEntryHasToNotAfterFrom()
    {
        var invalidEntry = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = WorkActivity,
            From = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero) // To == From, not a valid window
        };
        SetupDefaults(WorkEntry(8, 0, 16, 30), invalidEntry); // still 8.5h from the valid entry alone

        var loggerMock = new Mock<ILogger<BreakInsertionService>>();

        var summary = await CreateService(logger: loggerMock.Object).RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1); // the malformed entry is excluded, not counted or crashing
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(invalidEntry.Guid.ToString())),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task IgnoresPeople_WithoutTheNoteMarker()
    {
        SetupDefaults(WorkEntry(8, 0, 16, 30));
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>
            {
                new() { Guid = Worker, Note = "  Integration  ", Inactive = false } // trims + case-insensitive
            });

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
    }

    [Fact]
    public async Task Throws_WhenBreakActivityNameNotFound()
    {
        SetupDefaults();
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work }
            });

        var act = () => CreateService().RunAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Oběd*");
    }

    [Fact]
    public async Task ContinuesWithNextDay_WhenOneInsertFails()
    {
        var day2 = new DateOnly(2026, 8, 2);
        var entryDay2 = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = day2,
            Activity = WorkActivity,
            From = new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 2, 16, 30, 0, TimeSpan.Zero)
        };
        SetupDefaults(entryDay2, WorkEntry(8, 0, 16, 30));

        _client.SetupSequence(c => c.CreateTimeEntryAsync(
                It.IsAny<LogetoCreateTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"))
            .Returns(Task.CompletedTask);

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
        summary.Failed.Should().Be(1);
    }

    [Fact]
    public async Task SkipsDay_WhenAnEntryIsStillOpen()
    {
        var openEntry = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = WorkActivity,
            From = new DateTimeOffset(2026, 8, 3, 17, 0, 0, TimeSpan.Zero),
            To = null // still clocked in
        };
        SetupDefaults(WorkEntry(8, 0, 16, 30), openEntry); // 8.5 h closed work would otherwise qualify

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(0);
        summary.SkippedInProgress.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.IsAny<LogetoCreateTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogsWarning_WhenAPastDayHasAnOpenRecord()
    {
        var openEntry = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day, // 2026-08-03, before "today" — the worker never clocked out
            Activity = WorkActivity,
            From = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
            To = null
        };
        SetupDefaults(openEntry);

        var loggerMock = new Mock<ILogger<BreakInsertionService>>();

        await CreateService(logger: loggerMock.Object).RunAsync(CancellationToken.None);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("open record")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task DoesNotWarn_WhenTodayHasAnOpenRecord()
    {
        var openEntry = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Today, // worker is at work right now — expected, not an anomaly
            Activity = WorkActivity,
            From = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero),
            To = null
        };
        SetupDefaults(openEntry);

        var loggerMock = new Mock<ILogger<BreakInsertionService>>();

        var summary = await CreateService(logger: loggerMock.Object).RunAsync(CancellationToken.None);

        summary.SkippedInProgress.Should().Be(1);
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("open record")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task InsertsBreak_ForToday_WhenAllRecordsAreClosed()
    {
        SetupDefaults(WorkEntryOn(Today, 6, 0, 14, 30)); // 8.5 h, finished

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.Is<LogetoCreateTimeEntryRequest>(r =>
                r.Date == Today
                && r.From == "2026-08-04T11:00:00"
                && r.To == "2026-08-04T11:30:00"),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InsertsExactlyOneBreak_ForTwelveHourDayWorkedInTwoShifts()
    {
        // Two 6 h shifts with an hour between them: BuildSegments keeps them separate
        // (not adjacent), and ComputeBreakSlot returns a single slot regardless.
        SetupDefaults(WorkEntry(6, 0, 12, 0), WorkEntry(13, 0, 19, 0));

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.Is<LogetoCreateTimeEntryRequest>(r =>
                r.Date == Day
                && r.From == "2026-08-03T11:00:00" // preferred window sits strictly inside the morning shift
                && r.To == "2026-08-03T11:30:00"),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.IsAny<LogetoCreateTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
