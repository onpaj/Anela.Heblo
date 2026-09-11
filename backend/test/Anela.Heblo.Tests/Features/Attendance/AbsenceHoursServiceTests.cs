using Anela.Heblo.Application.Features.Attendance;
using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Domain.Features.Attendance;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Attendance;

public class AbsenceHoursServiceTests
{
    private static readonly Guid WorkActivity = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid VacationActivity = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SickActivity = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid Worker = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateOnly Day = new(2026, 8, 3);
    private static readonly DateOnly Today = new(2026, 8, 4); // matches the fixed "now" in CreateService

    private readonly Mock<ILogetoClient> _client = new();

    private AbsenceHoursService CreateService(
        AbsenceHoursOptions? options = null,
        ILogger<AbsenceHoursService>? logger = null,
        DateTimeOffset? now = null)
    {
        options ??= new AbsenceHoursOptions { StartDate = new DateOnly(2026, 8, 1) };

        // Fixed "now": 2026-08-04 08:00 Prague, so Prague date 2026-08-04 is `today`. The walk is
        // past-only, so with the default StartDate (2026-08-01) the window runs 2026-08-01 through
        // 2026-08-03.
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow())
            .Returns(now ?? new DateTimeOffset(2026, 8, 4, 6, 0, 0, TimeSpan.Zero));

        return new AbsenceHoursService(
            _client.Object,
            Options.Create(options),
            timeProvider.Object,
            logger ?? NullLogger<AbsenceHoursService>.Instance);
    }

    private void SetupDefaults(params LogetoTimeEntry[] entries) => SetupDefaults("integration 6,4", entries);

    private void SetupDefaults(string workerNote, params LogetoTimeEntry[] entries)
    {
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work },
                new() { Guid = VacationActivity, Name = "Dovolená", Type = LogetoActivityTypes.Absence },
                new() { Guid = SickActivity, Name = "Nemoc", Type = LogetoActivityTypes.Absence }
            });

        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>
            {
                new() { Guid = Worker, FirstName = "Petra", LastName = "Zilvarová", Note = workerNote, Inactive = false },
                new() { Guid = Guid.NewGuid(), Note = "somebody else", Inactive = false }
            });

        _client.Setup(c => c.GetTimeTrackingAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries.ToList());
    }

    private static LogetoTimeEntry EmptyAbsenceOn(DateOnly date, Guid? activity = null) => new()
    {
        Guid = Guid.NewGuid(),
        Person = Worker,
        Date = date,
        Activity = activity ?? VacationActivity
    };

    private static LogetoTimeEntry EmptyAbsence() => EmptyAbsenceOn(Day);

    private static LogetoTimeEntry WorkEntry(int fromHour, int toHour) => new()
    {
        Guid = Guid.NewGuid(),
        Person = Worker,
        Date = Day,
        Activity = WorkActivity,
        From = new DateTimeOffset(Day.Year, Day.Month, Day.Day, fromHour, 0, 0, TimeSpan.Zero),
        To = new DateTimeOffset(Day.Year, Day.Month, Day.Day, toHour, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public async Task FillsHours_ForLoneEmptyAbsenceDay()
    {
        // Arrange
        var absence = EmptyAbsence();
        SetupDefaults(absence);

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.HoursFilled.Should().Be(1);
        summary.RecordsScanned.Should().Be(1);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            absence.Guid,
            It.Is<LogetoTimeEntryRequest>(r =>
                r.Person == Worker
                && r.Activity == VacationActivity
                && r.Date == Day
                && r.Hours == "06:24:00"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FormatsDailyHours_AsHoursMinutesAndZeroSeconds()
    {
        // Arrange — Logeto rejects a non-zero seconds component; 6,4 h is 6 h 24 min.
        SetupDefaults("integration 6,4", EmptyAbsence());

        // Act
        await CreateService().RunAsync(CancellationToken.None);

        // Assert
        _client.Verify(c => c.UpdateTimeEntryAsync(
            It.IsAny<Guid>(),
            It.Is<LogetoTimeEntryRequest>(r => r.Hours == "06:24:00"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FormatsWholeHours_WithLeadingZeroAndZeroMinutes()
    {
        // Arrange
        SetupDefaults("integration 8", EmptyAbsence());

        // Act
        await CreateService().RunAsync(CancellationToken.None);

        // Assert
        _client.Verify(c => c.UpdateTimeEntryAsync(
            It.IsAny<Guid>(),
            It.Is<LogetoTimeEntryRequest>(r => r.Hours == "08:00:00"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PreservesEveryOtherFieldOnThePut()
    {
        // Arrange — the PUT is a full replacement, so every field but Hours is resent unchanged.
        var contract = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var subcontract = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var absence = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = VacationActivity,
            Description = "Dovolená",
            ExternalKey = "legacy-key",
            Billable = true,
            Contract = contract,
            Subcontract = subcontract
        };
        SetupDefaults(absence);

        // Act
        await CreateService().RunAsync(CancellationToken.None);

        // Assert
        _client.Verify(c => c.UpdateTimeEntryAsync(
            absence.Guid,
            It.Is<LogetoTimeEntryRequest>(r =>
                r.Description == "Dovolená"
                && r.ExternalKey == "legacy-key"
                && r.Billable == true
                && r.Contract == contract
                && r.Subcontract == subcontract),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IgnoresToday_BecauseTheWorkerMayStillEditIt()
    {
        // Arrange
        SetupDefaults(EmptyAbsenceOn(Today));

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.HoursFilled.Should().Be(0);
        summary.RecordsScanned.Should().Be(0);
        VerifyNoUpdate();
    }

    [Fact]
    public async Task RequestsTimeTracking_ForThePastOnlyWindow()
    {
        // Arrange
        SetupDefaults();
        var options = new AbsenceHoursOptions { StartDate = new DateOnly(2026, 1, 1) }; // far past — lookback governs

        // Act
        await CreateService(options).RunAsync(CancellationToken.None);

        // Assert — "now" is 2026-08-04; default lookback of 7 days → 2026-07-28 .. yesterday.
        _client.Verify(c => c.GetTimeTrackingAsync(
            new DateOnly(2026, 7, 28), new DateOnly(2026, 8, 3), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClampsWindowStart_ToStartDate_WhenLookbackReachesPastIt()
    {
        // Arrange
        SetupDefaults();
        var options = new AbsenceHoursOptions { StartDate = new DateOnly(2026, 8, 2), LookbackDays = 30 };

        // Act
        await CreateService(options).RunAsync(CancellationToken.None);

        // Assert
        _client.Verify(c => c.GetTimeTrackingAsync(
            new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 3), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoesNothing_WhenStartDateLeavesAnEmptyWindow()
    {
        // Arrange — StartDate is today, but the walk never enters today, so there is no window.
        SetupDefaults(EmptyAbsence());
        var options = new AbsenceHoursOptions { StartDate = Today };

        // Act
        var summary = await CreateService(options).RunAsync(CancellationToken.None);

        // Assert
        summary.HoursFilled.Should().Be(0);
        _client.Verify(c => c.GetTimeTrackingAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoUpdate();
    }

    [Fact]
    public async Task IgnoresAbsence_ThatAlreadyHasHours()
    {
        // Arrange
        var absence = EmptyAbsence();
        SetupDefaults(new LogetoTimeEntry
        {
            Guid = absence.Guid,
            Person = Worker,
            Date = Day,
            Activity = VacationActivity,
            Hours = "08:00:00"
        });

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.HoursFilled.Should().Be(0);
        summary.RecordsScanned.Should().Be(0);
        VerifyNoUpdate();
    }

    [Fact]
    public async Task IgnoresAbsence_ThatHasAFromToWindow()
    {
        // Arrange
        SetupDefaults(new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = VacationActivity,
            From = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)
        });

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.HoursFilled.Should().Be(0);
        summary.RecordsScanned.Should().Be(0);
        VerifyNoUpdate();
    }

    [Fact]
    public async Task IgnoresTimelessRecords_OfNonAbsenceActivities()
    {
        // Arrange — a Work record with neither window nor hours is not ours to fill.
        SetupDefaults(new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = WorkActivity
        });

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.HoursFilled.Should().Be(0);
        summary.RecordsScanned.Should().Be(0);
        summary.SkippedMixedDay.Should().Be(0);
        VerifyNoUpdate();
    }

    [Fact]
    public async Task SkipsMixedDay_WhenAnEmptyAbsenceSharesTheDayWithWork()
    {
        // Arrange — a half-day absence must not receive a full day's hours.
        SetupDefaults(EmptyAbsence(), WorkEntry(8, 12));
        var loggerMock = new Mock<ILogger<AbsenceHoursService>>();

        // Act
        var summary = await CreateService(logger: loggerMock.Object).RunAsync(CancellationToken.None);

        // Assert
        summary.SkippedMixedDay.Should().Be(1);
        summary.HoursFilled.Should().Be(0);
        VerifyNoUpdate();
        VerifyWarningContains(loggerMock, "Zilvarová");
    }

    [Fact]
    public async Task SkipsMixedDay_WhenAnEmptyAbsenceSharesTheDayWithAnAlreadyTimedAbsence()
    {
        // Arrange
        SetupDefaults(EmptyAbsence(), new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(),
            Person = Worker,
            Date = Day,
            Activity = SickActivity,
            Hours = "04:00:00"
        });

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.SkippedMixedDay.Should().Be(1);
        summary.HoursFilled.Should().Be(0);
        VerifyNoUpdate();
    }

    [Fact]
    public async Task SkipsAmbiguousDay_WhenTwoEmptyAbsencesShareTheDay()
    {
        // Arrange — the day's hours cannot be split between them without guessing.
        SetupDefaults(EmptyAbsence(), EmptyAbsenceOn(Day, SickActivity));

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.SkippedAmbiguous.Should().Be(1);
        summary.SkippedMixedDay.Should().Be(0);
        summary.RecordsScanned.Should().Be(2);
        summary.HoursFilled.Should().Be(0);
        VerifyNoUpdate();
    }

    [Fact]
    public async Task ReportsMixedDay_RatherThanAmbiguous_WhenTheDayIsBoth()
    {
        // Arrange — two empty absences *and* a work record. The guards run in spec order, so the
        // reported cause is the mixed day, which is the real one.
        SetupDefaults(EmptyAbsence(), EmptyAbsenceOn(Day, SickActivity), WorkEntry(8, 12));

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.SkippedMixedDay.Should().Be(1);
        summary.SkippedAmbiguous.Should().Be(0);
        VerifyNoUpdate();
    }

    [Fact]
    public async Task ReportsAmbiguous_RatherThanNoHours_WhenThePersonAlsoHasNoUvazek()
    {
        // Arrange — the day is unfillable regardless of the note, and the guards say so in order.
        SetupDefaults("integration", EmptyAbsence(), EmptyAbsenceOn(Day, SickActivity));

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.SkippedAmbiguous.Should().Be(1);
        summary.SkippedNoHours.Should().Be(0);
        VerifyNoUpdate();
    }

    [Fact]
    public async Task SkipsPerson_WhoseNoteCarriesNoHours_AndWarnsNamingThem()
    {
        // Arrange — the expected state until each person's Logeto note is updated.
        SetupDefaults("integration", EmptyAbsence());
        var loggerMock = new Mock<ILogger<AbsenceHoursService>>();

        // Act
        var summary = await CreateService(logger: loggerMock.Object).RunAsync(CancellationToken.None);

        // Assert
        summary.SkippedNoHours.Should().Be(1);
        summary.HoursFilled.Should().Be(0);
        VerifyNoUpdate();
        VerifyWarningContains(loggerMock, "Zilvarová");
    }

    [Fact]
    public async Task IgnoresPeople_WithoutTheNoteMarker()
    {
        // Arrange
        SetupDefaults("dovolená 6,4", EmptyAbsence());

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.RecordsScanned.Should().Be(0);
        VerifyNoUpdate();
    }

    [Fact]
    public async Task IgnoresInactivePeople()
    {
        // Arrange
        SetupDefaults(EmptyAbsence());
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>
            {
                new() { Guid = Worker, Note = "integration 6,4", Inactive = true }
            });

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.RecordsScanned.Should().Be(0);
        VerifyNoUpdate();
    }

    [Fact]
    public async Task FailingPut_IncrementsFailed_AndTheRunContinues()
    {
        // Arrange
        var failing = EmptyAbsenceOn(new DateOnly(2026, 8, 2));
        var succeeding = EmptyAbsence();
        SetupDefaults(failing, succeeding);

        _client.Setup(c => c.UpdateTimeEntryAsync(
                failing.Guid, It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Logeto said no"));

        // Act
        var summary = await CreateService().RunAsync(CancellationToken.None);

        // Assert
        summary.Failed.Should().Be(1);
        summary.HoursFilled.Should().Be(1);
        _client.Verify(c => c.UpdateTimeEntryAsync(
            succeeding.Guid, It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private void VerifyNoUpdate() => _client.Verify(c => c.UpdateTimeEntryAsync(
        It.IsAny<Guid>(), It.IsAny<LogetoTimeEntryRequest>(), It.IsAny<CancellationToken>()), Times.Never);

    private static void VerifyWarningContains(Mock<ILogger<AbsenceHoursService>> loggerMock, string expected) =>
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expected)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
}
