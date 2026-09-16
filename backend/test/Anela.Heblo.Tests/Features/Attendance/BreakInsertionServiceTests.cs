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
        BreakInsertionOptions? options = null, ILogger<BreakInsertionService>? logger = null, DateTimeOffset? now = null)
    {
        options ??= new BreakInsertionOptions
        {
            StartDate = new DateOnly(2026, 8, 1),
            BreakActivityName = "Oběd",
            ApiTimesAreUtc = false // tests use wall-clock times directly for readability
        };

        // Fixed "now": 2026-08-04 08:00 Prague, so Prague date 2026-08-04 is `today`. With the
        // default StartDate (2026-08-01) and default LookbackDays (7), the window therefore runs
        // 2026-08-01 (clamped up to StartDate) through 2026-08-04.
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow())
            .Returns(now ?? new DateTimeOffset(2026, 8, 4, 6, 0, 0, TimeSpan.Zero));

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
            It.Is<LogetoTimeEntryRequest>(r =>
                r.Person == Worker
                && r.Activity == BreakActivity
                && r.Date == Day
                && r.From == "2026-08-03T11:30:00"
                && r.To == "2026-08-03T12:00:00"
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
            Revision = 10,
            From = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 3, 12, 10, 0, TimeSpan.Zero)
        };
        // Already split *and* already touched: both work records outrank the break's revision,
        // so a syncing client has seen them and there is nothing left to do.
        SetupDefaults(
            WorkEntryRev(8, 0, 12, 0, revision: 20), existingBreak, WorkEntryRev(12, 10, 16, 30, revision: 21));

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(0);
        summary.SkippedExistingBreak.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            It.IsAny<Guid>(), It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task SelectsPerson_WhenNoteCarriesDailyHours()
    {
        // The note carries the person's úvazek: "integration 6,4". Break insertion used to
        // match the note with exact equality, which silently dropped this person.
        SetupDefaults(WorkEntry(8, 0, 16, 30));
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>
            {
                new() { Guid = Worker, Note = "integration 6,4", Inactive = false }
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
                It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
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
            It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
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
            It.Is<LogetoTimeEntryRequest>(r =>
                r.Date == Today
                && r.From == "2026-08-04T11:30:00"
                && r.To == "2026-08-04T12:00:00"),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InsertsExactlyOneBreak_ForTwelveHourDayWorkedInTwoShifts()
    {
        // Two shifts with a gap between them: BuildSegments keeps them separate
        // (not adjacent), and ComputeBreakSlot returns a single slot regardless.
        SetupDefaults(WorkEntry(6, 0, 12, 15), WorkEntry(13, 0, 19, 0));

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.Is<LogetoTimeEntryRequest>(r =>
                r.Date == Day
                && r.From == "2026-08-03T11:30:00" // preferred window sits strictly inside the morning shift
                && r.To == "2026-08-03T12:00:00"),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FallsBackToCenteredBreak_WhenShiftEndsExactlyAtPreferredWindowEnd()
    {
        // The preferred window (11:30-12:00) must sit strictly inside the segment, so a shift
        // ending exactly at 12:00 touches the window's edge and falls back to a break centered
        // in the segment instead of the preferred window.
        SetupDefaults(WorkEntry(6, 0, 12, 0));

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.Is<LogetoTimeEntryRequest>(r =>
                r.Date == Day
                && r.From == "2026-08-03T08:45:00"
                && r.To == "2026-08-03T09:15:00"),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessesEachDayInTheWindowIndependently_WhenOneRunSpansMultipleDays()
    {
        // 2026-08-02: open record — should be skipped without affecting the other two days.
        var openEntry = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = new DateOnly(2026, 8, 2),
            Activity = WorkActivity,
            From = new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero),
            To = null
        };
        // 2026-08-03 (Day): 8.5 h closed work — qualifies for a break.
        // 2026-08-04 (Today): 2 h closed work — below the 6 h threshold.
        SetupDefaults(openEntry, WorkEntry(8, 0, 16, 30), WorkEntryOn(Today, 8, 0, 10, 0));

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.DaysScanned.Should().Be(3);
        summary.SkippedInProgress.Should().Be(1);
        summary.BreaksInserted.Should().Be(1);
        summary.SkippedBelowThreshold.Should().Be(1);
    }

    [Fact]
    public async Task IgnoresEntries_OutsideTheComputedWindow_EvenWhenTheMockReturnsThem()
    {
        // The mock (via SetupDefaults) returns every entry regardless of the requested range, so
        // only the service's own `e.Date >= from && e.Date <= today` filter can exclude this one.
        var outsideDate = new DateOnly(2026, 7, 20); // before the clamped `from` of 2026-08-01
        var outsideEntry = WorkEntryOn(outsideDate, 8, 0, 16, 30); // 8.5 h — would qualify if scanned
        SetupDefaults(outsideEntry, WorkEntry(8, 0, 16, 30)); // Day (2026-08-03) is inside the window

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.DaysScanned.Should().Be(1); // only the in-window day was scanned
        summary.BreaksInserted.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.Is<LogetoTimeEntryRequest>(r => r.Date == outsideDate),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestsTimeTracking_ForTheConfiguredLookbackDays_WhenNotDefault()
    {
        SetupDefaults();
        var options = new BreakInsertionOptions
        {
            StartDate = new DateOnly(2026, 1, 1), // far enough in the past not to clamp
            BreakActivityName = "Oběd",
            ApiTimesAreUtc = false,
            LookbackDays = 2
        };

        await CreateService(options).RunAsync(CancellationToken.None);

        // "now" is 2026-08-04; lookback of 2 days → window starts 2026-08-02.
        _client.Verify(c => c.GetTimeTrackingAsync(
            new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 4), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ComputesToday_FromPragueTime_NotUtc()
    {
        SetupDefaults();

        // 2026-08-04T22:30:00Z is 2026-08-05 00:30 in Prague (UTC+2 in August) — a different
        // calendar day than the UTC instant, so this is the one place a timezone bug would hide.
        var utcNow = new DateTimeOffset(2026, 8, 4, 22, 30, 0, TimeSpan.Zero);

        await CreateService(now: utcNow).RunAsync(CancellationToken.None);

        // Default lookback of 7 days from Prague "today" 2026-08-05 → 2026-07-29, clamped up to
        // the default StartDate of 2026-08-01.
        _client.Verify(c => c.GetTimeTrackingAsync(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 5), It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- helpers for the post-split re-read ---------------------------------------------------

    private static LogetoTimeEntry WorkEntryRev(
        int fromHour, int fromMin, int toHour, int toMin, int revision) => new()
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = WorkActivity,
            Revision = revision,
            From = new DateTimeOffset(2026, 8, 3, fromHour, fromMin, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 3, toHour, toMin, 0, TimeSpan.Zero)
        };

    private static LogetoTimeEntry BreakEntryRev(
        int fromHour, int fromMin, int toHour, int toMin, int revision) => new()
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = BreakActivity,
            Revision = revision,
            ExternalKey = $"autobreak-{Worker}-2026-08-03",
            From = new DateTimeOffset(2026, 8, 3, fromHour, fromMin, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 3, toHour, toMin, 0, TimeSpan.Zero)
        };

    private static LogetoTimeEntry WorkEntryOnRev(
        DateOnly date, int fromHour, int fromMin, int toHour, int toMin, int revision) => new()
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = date,
            Activity = WorkActivity,
            Revision = revision,
            From = new DateTimeOffset(date.Year, date.Month, date.Day, fromHour, fromMin, 0, TimeSpan.Zero),
            To = new DateTimeOffset(date.Year, date.Month, date.Day, toHour, toMin, 0, TimeSpan.Zero)
        };

    private static LogetoTimeEntry BreakEntryOnRev(
        DateOnly date, int fromHour, int fromMin, int toHour, int toMin, int revision) => new()
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = date,
            Activity = BreakActivity,
            Revision = revision,
            ExternalKey = $"autobreak-{Worker}-{date:yyyy-MM-dd}",
            From = new DateTimeOffset(date.Year, date.Month, date.Day, fromHour, fromMin, 0, TimeSpan.Zero),
            To = new DateTimeOffset(date.Year, date.Month, date.Day, toHour, toMin, 0, TimeSpan.Zero)
        };

    /// <summary>A break a worker entered themselves — no <c>autobreak-</c> key, never split by us.</summary>
    private static LogetoTimeEntry ManualBreakRev(
        int fromHour, int fromMin, int toHour, int toMin, int revision) => new()
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = BreakActivity,
            Revision = revision,
            ExternalKey = null,
            From = new DateTimeOffset(2026, 8, 3, fromHour, fromMin, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 3, toHour, toMin, 0, TimeSpan.Zero)
        };

    /// <summary>What the day looks like once Logeto's merge=true split has run. Registered after
    /// the catch-all setup so Moq matches this narrower one for the single-day re-read.</summary>
    private void SetupPostSplit(params LogetoTimeEntry[] entries) =>
        _client.Setup(c => c.GetTimeTrackingAsync(Day, Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries.ToList());

    // --- the touch ----------------------------------------------------------------------------

    [Fact]
    public async Task TouchesBothWorkRecords_AroundTheBreak_AfterTheSplit()
    {
        // Arrange — Logeto rewrote the original in place (revision left behind at 5) and created
        // the first half (13) plus the break (14).
        var firstHalf = WorkEntryRev(5, 20, 11, 30, revision: 13);
        var afterBreak = WorkEntryRev(12, 0, 13, 19, revision: 5);
        SetupDefaults(WorkEntry(5, 20, 13, 19));
        SetupPostSplit(firstHalf, BreakEntryRev(11, 30, 12, 0, revision: 14), afterBreak);

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.BreaksInserted.Should().Be(1);
        summary.RecordsTouched.Should().Be(2);

        _client.Verify(c => c.UpdateTimeEntryAsync(
            afterBreak.Guid,
            It.Is<LogetoTimeEntryRequest>(r =>
                r.From == "2026-08-03T12:00:00" && r.To == "2026-08-03T13:19:00"),
            It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            firstHalf.Guid,
            It.Is<LogetoTimeEntryRequest>(r =>
                r.From == "2026-08-03T05:20:00" && r.To == "2026-08-03T11:30:00"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResendsTheRecordUnchanged_WhenTouching()
    {
        // Arrange — a touch must not alter anything; only the Revision moves, server-side.
        var contract = Guid.NewGuid();
        var subcontract = Guid.NewGuid();
        var afterBreak = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = WorkActivity,
            Revision = 5,
            From = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 3, 13, 19, 0, TimeSpan.Zero),
            Billable = true,
            Description = "Ruční výroba",
            ExternalKey = "payroll-42",
            Contract = contract,
            Subcontract = subcontract
        };
        SetupDefaults(WorkEntry(5, 20, 13, 19));
        SetupPostSplit(BreakEntryRev(11, 30, 12, 0, revision: 14), afterBreak);

        // Act
        await CreateService().RunAsync(CancellationToken.None);

        // Assert
        _client.Verify(c => c.UpdateTimeEntryAsync(
            afterBreak.Guid,
            It.Is<LogetoTimeEntryRequest>(r =>
                r.Person == Worker
                && r.Activity == WorkActivity
                && r.Date == Day
                && r.From == "2026-08-03T12:00:00"
                && r.To == "2026-08-03T13:19:00"
                && r.Billable
                && r.Description == "Ruční výroba"
                && r.ExternalKey == "payroll-42"
                && r.Contract == contract
                && r.Subcontract == subcontract),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoesNotTouchWorkRecords_ThatTheSplitDidNotProduce()
    {
        // Arrange — an unrelated evening shift is nowhere near the break.
        var eveningShift = WorkEntryRev(16, 0, 19, 0, revision: 4);
        SetupDefaults(WorkEntry(5, 20, 13, 19), WorkEntry(16, 0, 19, 0));
        SetupPostSplit(
            WorkEntryRev(5, 20, 11, 30, revision: 13),
            BreakEntryRev(11, 30, 12, 0, revision: 14),
            WorkEntryRev(12, 0, 13, 19, revision: 5),
            eveningShift);

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.RecordsTouched.Should().Be(2);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            eveningShift.Guid, It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TouchesAStaleDay_WhenAnEarlierRunSplitItButNeverTouchedIt()
    {
        // Arrange — every day this job created before it learned to touch looks like this.
        var afterBreak = WorkEntryRev(12, 0, 13, 50, revision: 5);
        SetupDefaults(
            WorkEntryRev(6, 43, 11, 30, revision: 13),
            BreakEntryRev(11, 30, 12, 0, revision: 14),
            afterBreak);

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.RecordsTouched.Should().Be(2);
        summary.BreaksInserted.Should().Be(0);
        summary.SkippedExistingBreak.Should().Be(0);

        _client.Verify(c => c.CreateTimeEntryAsync(
            It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            afterBreak.Guid, It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WarnsAndTouchesNothing_WhenTheReReadShowsNoBreak()
    {
        // Arrange — the split did not come back in the re-read, so there is nothing to refresh yet.
        var logger = new Mock<ILogger<BreakInsertionService>>();
        SetupDefaults(WorkEntry(5, 20, 13, 19));
        SetupPostSplit(WorkEntryRev(5, 20, 13, 19, revision: 5));

        // Act
        var summary = await CreateService(logger: logger.Object).RunAsync(CancellationToken.None);

        // Assert
        summary.BreaksInserted.Should().Be(1);
        summary.RecordsTouched.Should().Be(0);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            It.IsAny<Guid>(), It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        logger.Verify(l => l.Log(
            LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("no work record adjacent to it")),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task DoesNotTouchAnything_WhenTheBreakWasEnteredByTheWorker()
    {
        // Arrange — the worker clocked a full day, then added their own lunch break afterwards.
        // The account-wide Revision counter therefore leaves both work records "below" the break,
        // which looks exactly like a stale day — but we never split this day, so nothing is stale.
        var morning = WorkEntryRev(8, 0, 12, 0, revision: 13);
        var afternoon = WorkEntryRev(12, 30, 16, 30, revision: 14);
        SetupDefaults(morning, ManualBreakRev(12, 0, 12, 30, revision: 20), afternoon);

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.SkippedExistingBreak.Should().Be(1);
        summary.RecordsTouched.Should().Be(0);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            It.IsAny<Guid>(), It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HealsOnlyItsOwnBreak_WhenTheDayAlsoCarriesAManualOne()
    {
        // Arrange — a manual morning break sits between two work records we must leave alone; our
        // own afternoon break, further along the day, has two stale neighbours that do need it.
        var manualBefore = WorkEntryRev(8, 0, 10, 0, revision: 13);
        var manualAfter = WorkEntryRev(10, 15, 11, 0, revision: 14);
        var ourBefore = WorkEntryRev(11, 30, 12, 0, revision: 15);
        var ourAfter = WorkEntryRev(12, 30, 16, 30, revision: 5);
        SetupDefaults(
            manualBefore,
            ManualBreakRev(10, 0, 10, 15, revision: 20),
            manualAfter,
            ourBefore,
            BreakEntryRev(12, 0, 12, 30, revision: 21),
            ourAfter);

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert — only the records adjacent to our own break are written.
        summary.RecordsTouched.Should().Be(2);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            ourBefore.Guid, It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            ourAfter.Guid, It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            manualBefore.Guid, It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            manualAfter.Guid, It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TouchesTheSharedRecordOnce_WhenItSitsBetweenTwoOfOurBreaks()
    {
        // Arrange — the middle segment is adjacent to the first break's end and the second break's
        // start, so a naive per-break loop would PUT it twice.
        var middle = WorkEntryRev(12, 0, 15, 0, revision: 5);
        SetupDefaults(
            WorkEntryRev(8, 0, 11, 30, revision: 13),
            BreakEntryRev(11, 30, 12, 0, revision: 20),
            middle,
            BreakEntryRev(15, 0, 15, 15, revision: 21),
            WorkEntryRev(15, 15, 17, 0, revision: 14));

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert — three distinct records, three writes, not four.
        summary.RecordsTouched.Should().Be(3);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            middle.Guid, It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DoesNotReportAFailedInsert_WhenOnlyTheFollowUpTouchFails()
    {
        // Arrange — the break lands, then Logeto rejects the touch.
        SetupDefaults(WorkEntry(5, 20, 13, 19));
        SetupPostSplit(
            WorkEntryRev(5, 20, 11, 30, revision: 13),
            BreakEntryRev(11, 30, 12, 0, revision: 14),
            WorkEntryRev(12, 0, 13, 19, revision: 5));
        _client.Setup(c => c.UpdateTimeEntryAsync(
                It.IsAny<Guid>(), It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Logeto rejected the write"));

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert — the insert succeeded, so it must not be counted as a failed day.
        summary.BreaksInserted.Should().Be(1);
        summary.Failed.Should().Be(0);
        summary.TouchFailed.Should().Be(1);
    }

    [Fact]
    public async Task CountsTouchesThatAlreadyLanded_WhenALaterTouchInTheSameDayFails()
    {
        // Arrange — first PUT succeeds, second throws. The first write is real and must be counted.
        var firstHalf = WorkEntryRev(5, 20, 11, 30, revision: 13);
        var afterBreak = WorkEntryRev(12, 0, 13, 19, revision: 5);
        SetupDefaults(WorkEntry(5, 20, 13, 19));
        SetupPostSplit(firstHalf, BreakEntryRev(11, 30, 12, 0, revision: 14), afterBreak);
        _client.Setup(c => c.UpdateTimeEntryAsync(
                afterBreak.Guid, It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Logeto rejected the second write"));

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.RecordsTouched.Should().Be(1);
        summary.TouchFailed.Should().Be(1);
        summary.Failed.Should().Be(0);
    }

    [Fact]
    public async Task CountsAHealedDaySeparately_FromDaysThatWereAlreadyFine()
    {
        // Arrange — one stale day, and one whose neighbours both already outrank the break (so it
        // was touched on an earlier run). Day-level buckets must not overlap.
        var staleDay = new DateOnly(2026, 8, 2);
        SetupDefaults(
            WorkEntryOnRev(staleDay, 6, 43, 11, 30, revision: 13),
            BreakEntryOnRev(staleDay, 11, 30, 12, 0, revision: 14),
            WorkEntryOnRev(staleDay, 12, 0, 13, 50, revision: 5),
            WorkEntryRev(8, 0, 12, 0, revision: 20),
            BreakEntryRev(12, 0, 12, 30, revision: 19),
            WorkEntryRev(12, 30, 16, 30, revision: 22));

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.DaysHealed.Should().Be(1);
        summary.SkippedExistingBreak.Should().Be(1);
        summary.RecordsTouched.Should().Be(2);
        summary.DaysScanned.Should().Be(2);
    }
}
