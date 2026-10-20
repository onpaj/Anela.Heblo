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
            new BreakInsertionRunGate(),
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
            false,
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
            false,
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
            false,
            It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.Is<LogetoTimeEntryRequest>(r => r.Activity == BreakActivity),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
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
            false,
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

    // --- recreating the split -----------------------------------------------------------------
    //
    // Logeto's phone app never picks up a record rewritten through the API — no API write moves
    // TimestampChanged — so the work around a break is recreated as brand-new records and the
    // originals deleted, instead of being split in place.

    private static readonly string OwnBreakKey = $"autobreak-{Worker}-2026-08-03";

    private static string PieceKey(string startHhmm, LogetoTimeEntry source) =>
        $"{OwnBreakKey}-{startHhmm}-{source.Guid.ToString("N")[..8]}";

    private static LogetoTimeEntry KeylessWork(int fromHour, int fromMin, int toHour, int toMin) =>
        WorkEntry(fromHour, fromMin, toHour, toMin);

    private static LogetoTimeEntry KeyedWork(int fromHour, int fromMin, int toHour, int toMin, string key) => new()
    {
        Guid = Guid.NewGuid(),
        Person = Worker,
        Date = Day,
        Activity = WorkActivity,
        ExternalKey = key,
        From = new DateTimeOffset(2026, 8, 3, fromHour, fromMin, 0, TimeSpan.Zero),
        To = new DateTimeOffset(2026, 8, 3, toHour, toMin, 0, TimeSpan.Zero)
    };

    private static LogetoTimeEntry OwnBreak(int fromHour, int fromMin, int toHour, int toMin) =>
        BreakEntryOnDay(fromHour, fromMin, toHour, toMin, OwnBreakKey);

    private static LogetoTimeEntry ManualBreak(int fromHour, int fromMin, int toHour, int toMin) =>
        BreakEntryOnDay(fromHour, fromMin, toHour, toMin, key: null);

    private static LogetoTimeEntry BreakEntryOnDay(
        int fromHour, int fromMin, int toHour, int toMin, string? key) => new()
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = BreakActivity,
            ExternalKey = key,
            From = new DateTimeOffset(2026, 8, 3, fromHour, fromMin, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 3, toHour, toMin, 0, TimeSpan.Zero)
        };

    /// <summary>Records every create and delete in call order, so tests can assert sequencing.</summary>
    private List<string> RecordWrites()
    {
        var calls = new List<string>();
        _client.Setup(c => c.CreateTimeEntryAsync(
                It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<LogetoTimeEntryRequest, bool, CancellationToken>((r, _, _) =>
                calls.Add($"create {r.From?[11..16]}-{r.To?[11..16]} {r.ExternalKey}"))
            .Returns(Task.CompletedTask);
        _client.Setup(c => c.DeleteTimeEntryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((g, _) => calls.Add($"delete {g}"))
            .Returns(Task.CompletedTask);
        return calls;
    }

    [Fact]
    public async Task RecreatesTheWorkAroundANewBreak_CreatingEverythingBeforeDeletingTheOriginal()
    {
        // Arrange
        var original = KeylessWork(8, 0, 16, 30);
        SetupDefaults(original);
        var calls = RecordWrites();

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.BreaksInserted.Should().Be(1);
        summary.RecordsRecreated.Should().Be(1);
        calls.Should().Equal(
            $"create 11:30-12:00 {OwnBreakKey}",
            $"create 08:00-11:30 {PieceKey("0800", original)}",
            $"create 12:00-16:30 {PieceKey("1200", original)}",
            $"delete {original.Guid}");
    }

    [Fact]
    public async Task NeverAsksLogetoToMerge_SinceAMergeRewritesRecordsInPlace()
    {
        // Arrange
        SetupDefaults(KeylessWork(8, 0, 16, 30));
        RecordWrites();

        // Act
        await CreateService().RunAsync(CancellationToken.None);

        // Assert
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.IsAny<LogetoTimeEntryRequest>(), true, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CopiesTheOriginalsFields_IntoTheRecreatedRecords()
    {
        // Arrange
        var contract = Guid.NewGuid();
        var subcontract = Guid.NewGuid();
        var original = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = WorkActivity,
            From = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 3, 16, 30, 0, TimeSpan.Zero),
            Billable = true,
            Description = "Ruční výroba",
            Contract = contract,
            Subcontract = subcontract
        };
        SetupDefaults(original);
        RecordWrites();

        // Act
        await CreateService().RunAsync(CancellationToken.None);

        // Assert
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.Is<LogetoTimeEntryRequest>(r =>
                r.Person == Worker
                && r.Activity == WorkActivity
                && r.Date == Day
                && r.From == "2026-08-03T12:00:00"
                && r.To == "2026-08-03T16:30:00"
                && r.Billable
                && r.Description == "Ruční výroba"
                && r.Contract == contract
                && r.Subcontract == subcontract
                && r.ExternalKey == PieceKey("1200", original)),
            false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KeepsTheOriginal_WhenCreatingAReplacementFails()
    {
        // Arrange — deleting before every replacement exists would lose worked time.
        var original = KeylessWork(8, 0, 16, 30);
        SetupDefaults(original);
        RecordWrites();
        _client.Setup(c => c.CreateTimeEntryAsync(
                It.Is<LogetoTimeEntryRequest>(r => r.ExternalKey == PieceKey("1200", original)),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert — the break is in; the day is left for the next run to finish, not counted as a failed insert.
        summary.BreaksInserted.Should().Be(1);
        summary.RecreateFailed.Should().Be(1);
        summary.Failed.Should().Be(0);
        _client.Verify(c => c.DeleteTimeEntryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecreatesBothHalves_OfADaySplitByAnEarlierMergeRun()
    {
        // Arrange — every day this job split with merge=true looks like this, and phones still
        // show the pre-split record because neither half ever moved TimestampChanged.
        var before = KeylessWork(6, 23, 11, 30);
        var after = KeylessWork(12, 0, 13, 32);
        SetupDefaults(before, OwnBreak(11, 30, 12, 0), after);
        var calls = RecordWrites();

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.BreaksInserted.Should().Be(0);
        summary.DaysHealed.Should().Be(1);
        summary.RecordsRecreated.Should().Be(2);
        calls.Should().Equal(
            $"create 06:23-11:30 {PieceKey("0623", before)}",
            $"create 12:00-13:32 {PieceKey("1200", after)}",
            $"delete {before.Guid}",
            $"delete {after.Guid}");
    }

    [Fact]
    public async Task FinishesAnInterruptedRun_WithoutRecreatingWhatAlreadyExists()
    {
        // Arrange — the break and the first replacement landed, then the run died.
        var original = KeylessWork(8, 0, 16, 30);
        SetupDefaults(original, OwnBreak(11, 30, 12, 0), KeyedWork(8, 0, 11, 30, PieceKey("0800", original)));
        var calls = RecordWrites();

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.DaysHealed.Should().Be(1);
        calls.Should().Equal(
            $"create 12:00-16:30 {PieceKey("1200", original)}",
            $"delete {original.Guid}");
    }

    [Fact]
    public async Task LeavesADayAlone_OnceItsWorkHasBeenRecreated()
    {
        // Arrange — our own replacements border the break; only one cut into by a break is redone.
        SetupDefaults(
            KeyedWork(8, 0, 11, 30, $"{OwnBreakKey}-0800-0a1b2c3d"),
            OwnBreak(11, 30, 12, 0),
            KeyedWork(12, 0, 16, 30, $"{OwnBreakKey}-1200-0a1b2c3d"));
        var calls = RecordWrites();

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.SkippedExistingBreak.Should().Be(1);
        summary.DaysHealed.Should().Be(0);
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task DoesNotTouchADay_WhoseOnlyBreakTheWorkerEnteredThemselves()
    {
        // Arrange
        SetupDefaults(KeylessWork(8, 0, 12, 0), ManualBreak(12, 0, 12, 30), KeylessWork(12, 30, 16, 30));
        var calls = RecordWrites();

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.SkippedExistingBreak.Should().Be(1);
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RecreatesOnlyTheWorkAroundItsOwnBreak_WhenTheDayAlsoCarriesAManualOne()
    {
        // Arrange
        var beforeOwn = KeylessWork(6, 0, 11, 30);
        var betweenBreaks = KeylessWork(12, 0, 15, 0);
        var afterManual = KeylessWork(15, 15, 17, 0);
        SetupDefaults(beforeOwn, OwnBreak(11, 30, 12, 0), betweenBreaks, ManualBreak(15, 0, 15, 15), afterManual);
        var calls = RecordWrites();

        // Act
        await CreateService().RunAsync(CancellationToken.None);

        // Assert
        calls.Should().Equal(
            $"create 06:00-11:30 {PieceKey("0600", beforeOwn)}",
            $"create 12:00-15:00 {PieceKey("1200", betweenBreaks)}",
            $"delete {beforeOwn.Guid}",
            $"delete {betweenBreaks.Guid}");
    }

    [Fact]
    public async Task LeavesWorkAwayFromTheBreak_AndRecordsOwnedByAnotherIntegration_Alone()
    {
        // Arrange
        var before = KeylessWork(6, 0, 11, 30);
        var foreignKeyed = KeyedWork(12, 0, 13, 0, "payroll-42");
        var evening = KeylessWork(16, 0, 19, 0);
        SetupDefaults(before, OwnBreak(11, 30, 12, 0), foreignKeyed, evening);
        var calls = RecordWrites();

        // Act
        await CreateService().RunAsync(CancellationToken.None);

        // Assert
        calls.Should().Equal(
            $"create 06:00-11:30 {PieceKey("0600", before)}",
            $"delete {before.Guid}");
    }

    [Fact]
    public async Task RecreatesItsOwnReplacement_WhenABreakIsReinsertedIntoIt()
    {
        // Arrange — the worker deleted our break, so tonight's break lands inside a record we
        // created last time. It must be recreated like any other, or the day overlaps for good.
        var morning = KeyedWork(8, 0, 11, 30, $"{OwnBreakKey}-0800-0a1b2c3d");
        var afternoon = KeyedWork(12, 0, 16, 30, $"{OwnBreakKey}-1200-0a1b2c3d");
        SetupDefaults(morning, afternoon);
        var calls = RecordWrites();

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert — the preferred window straddles the gap, so the break centres in the afternoon.
        summary.BreaksInserted.Should().Be(1);
        calls.Should().Equal(
            $"create 14:00-14:30 {OwnBreakKey}",
            $"create 12:00-14:00 {PieceKey("1200", afternoon)}",
            $"create 14:30-16:30 {PieceKey("1430", afternoon)}",
            $"delete {afternoon.Guid}");
    }

    [Fact]
    public async Task SkipsDay_WhenTheBreakWouldLandOnARecordAnotherIntegrationOwns()
    {
        // Arrange — we may not recreate a foreign-keyed record, so a break over it would just overlap.
        SetupDefaults(KeyedWork(8, 0, 16, 30, "payroll-42"));
        var calls = RecordWrites();

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.BreaksInserted.Should().Be(0);
        summary.SkippedNoSlot.Should().Be(1);
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task GivesEachSourceItsOwnReplacementKey_WhenTwoOverlappingRecordsSpanTheBreak()
    {
        // Arrange — both tails start at the break's end; a start-only key would collide and a
        // rerun would then skip one tail and delete its original, losing worked time.
        var first = KeylessWork(8, 0, 13, 0);
        var second = KeylessWork(9, 0, 15, 0);
        SetupDefaults(first, second);
        var calls = RecordWrites();

        // Act
        await CreateService().RunAsync(CancellationToken.None);

        // Assert
        calls.Should().Contain($"create 12:00-13:00 {PieceKey("1200", first)}");
        calls.Should().Contain($"create 12:00-15:00 {PieceKey("1200", second)}");
    }

    [Fact]
    public async Task CountsRecreateFailures_SeparatelyFromDaysThatWereAlreadyFine()
    {
        // Arrange
        SetupDefaults(KeylessWork(6, 0, 11, 30), OwnBreak(11, 30, 12, 0));
        RecordWrites();
        _client.Setup(c => c.DeleteTimeEntryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.RecreateFailed.Should().Be(1);
        summary.SkippedExistingBreak.Should().Be(0);
        summary.DaysHealed.Should().Be(0);
        summary.Failed.Should().Be(0);
    }

    [Fact]
    public async Task DeletesNoOriginal_WhenASecondOriginalsReplacementFails()
    {
        // Arrange — the first original is fully replaced before the second's create throws.
        var before = KeylessWork(6, 0, 11, 30);
        var after = KeylessWork(12, 0, 13, 30);
        SetupDefaults(before, OwnBreak(11, 30, 12, 0), after);
        var calls = RecordWrites();
        _client.Setup(c => c.CreateTimeEntryAsync(
                It.Is<LogetoTimeEntryRequest>(r => r.ExternalKey == PieceKey("1200", after)),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert — nothing is deleted until every replacement exists; the rerun skips the one made.
        summary.RecreateFailed.Should().Be(1);
        calls.Should().Equal($"create 06:00-11:30 {PieceKey("0600", before)}");
    }

    [Fact]
    public async Task FinishesAPartialDelete_OnTheNextRun_WithoutRecreatingAnything()
    {
        // Arrange — both replacements exist and the first original is gone; deleting the second failed.
        var after = KeylessWork(12, 0, 13, 30);
        SetupDefaults(
            KeyedWork(6, 0, 11, 30, $"{OwnBreakKey}-0600-0a1b2c3d"),
            OwnBreak(11, 30, 12, 0),
            KeyedWork(12, 0, 13, 30, PieceKey("1200", after)),
            after);
        var calls = RecordWrites();

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.DaysHealed.Should().Be(1);
        summary.RecordsRecreated.Should().Be(1);
        calls.Should().Equal($"delete {after.Guid}");
    }

    [Fact]
    public async Task StopsTheRun_WhenCancelledWhileRecreating_InsteadOfCountingAFailure()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        SetupDefaults(KeylessWork(6, 0, 11, 30), OwnBreak(11, 30, 12, 0));
        RecordWrites();
        _client.Setup(c => c.CreateTimeEntryAsync(
                It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new TaskCanceledException());

        // Act
        var act = () => CreateService().RunAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StopsTheRun_WhenCancelledWhileInsertingABreak_InsteadOfCountingAFailure()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        SetupDefaults(KeylessWork(8, 0, 16, 30));
        RecordWrites();
        _client.Setup(c => c.CreateTimeEntryAsync(
                It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new TaskCanceledException());

        // Act
        var act = () => CreateService().RunAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
