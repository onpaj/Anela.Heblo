using Anela.Heblo.Application.Features.Attendance.Overtime;
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Overtime;

public class OvertimeCalculationServiceTests
{
    private static readonly Guid WorkActivity = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BreakActivity = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid VacationActivity = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid CompTimeActivity = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<ILogetoClient> _client = new();
    private readonly Mock<IContractHoursProvider> _contractHours = new();

    public OvertimeCalculationServiceTests()
    {
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work },
                new() { Guid = BreakActivity, Name = "Přestávka", Type = LogetoActivityTypes.Break },
                new() { Guid = VacationActivity, Name = "Dovolená", Type = LogetoActivityTypes.Work },
                new() { Guid = CompTimeActivity, Name = "Náhradní volno", Type = LogetoActivityTypes.Work }
            });
        _contractHours.Setup(p => p.GetDailyHoursAsync(Person, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(6.4m);
    }

    private OvertimeCalculationService CreateSut()
    {
        var options = new OvertimeOptions
        {
            ActivityCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dovolená"] = "Vacation",
                ["Náhradní volno"] = "CompTime"
            }
        };
        return new OvertimeCalculationService(
            _client.Object, _contractHours.Object, Options.Create(options),
            NullLogger<OvertimeCalculationService>.Instance);
    }

    private static OvertimeEmployee Employee(DateOnly? baseline = null) => new()
    {
        PersonId = Person,
        DisplayName = "Pepina",
        BaselineDate = baseline ?? new DateOnly(2026, 8, 1),
        IsActive = true
    };

    private void SetupEntries(params LogetoTimeEntry[] entries)
        => _client.Setup(c => c.GetTimeTrackingAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries.ToList());

    private static LogetoTimeEntry Entry(Guid activity, DateOnly date, int fromH, int toH) => new()
    {
        Guid = Guid.NewGuid(),
        Person = Person,
        Date = date,
        Activity = activity,
        From = new DateTimeOffset(date.Year, date.Month, date.Day, fromH, 0, 0, TimeSpan.Zero),
        To = new DateTimeOffset(date.Year, date.Month, date.Day, toH, 0, 0, TimeSpan.Zero)
    };

    private static LogetoTimeEntry HoursEntry(Guid activity, DateOnly date, string hours) => new()
    {
        Guid = Guid.NewGuid(),
        Person = Person,
        Date = date,
        Activity = activity,
        Hours = hours
    };

    [Fact]
    public async Task ComputesDelta_WorkAndVacationCredited_BreaksExcluded()
    {
        // August 2026 has 21 working days → required = 21 × 6.4 = 134.4
        SetupEntries(
            Entry(WorkActivity, new DateOnly(2026, 8, 3), 8, 14),          // 6h work
            Entry(BreakActivity, new DateOnly(2026, 8, 3), 11, 12),        // 1h break — ignored
            HoursEntry(VacationActivity, new DateOnly(2026, 8, 4), "06:24:00")); // 6.4h vacation

        var result = await CreateSut().ComputeMonthAsync(2026, 8, new[] { Employee() }, CancellationToken.None);

        var row = result.Single();
        row.RequiredHours.Should().Be(134.40m);
        row.WorkedHours.Should().Be(6.00m);
        row.VacationHours.Should().Be(6.40m);
        row.DeltaHours.Should().Be(6.00m + 6.40m - 134.40m);
    }

    [Fact]
    public async Task CompTime_IsNotCredited()
    {
        SetupEntries(HoursEntry(CompTimeActivity, new DateOnly(2026, 8, 3), "06:24:00"));

        var result = await CreateSut().ComputeMonthAsync(2026, 8, new[] { Employee() }, CancellationToken.None);

        var row = result.Single();
        row.CompTimeHours.Should().Be(6.40m);
        row.DeltaHours.Should().Be(-134.40m);   // comp time gives no credit
    }

    [Fact]
    public async Task EntriesBeforeBaseline_AreIgnored_AndRequiredCountsFromBaseline()
    {
        // Baseline 2026-08-17 (Mon): 11 working days remain → required = 11 × 6.4 = 70.4
        SetupEntries(
            Entry(WorkActivity, new DateOnly(2026, 8, 10), 8, 16),   // before baseline — ignored
            Entry(WorkActivity, new DateOnly(2026, 8, 18), 8, 16));  // 8h counted

        var result = await CreateSut().ComputeMonthAsync(
            2026, 8, new[] { Employee(baseline: new DateOnly(2026, 8, 17)) }, CancellationToken.None);

        var row = result.Single();
        row.RequiredHours.Should().Be(70.40m);
        row.WorkedHours.Should().Be(8.00m);
    }

    [Fact]
    public async Task BaselineAfterMonth_SkipsEmployee()
    {
        SetupEntries();

        var result = await CreateSut().ComputeMonthAsync(
            2026, 8, new[] { Employee(baseline: new DateOnly(2026, 9, 1)) }, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingContractHours_ProducesWarning_AndNullContract()
    {
        _contractHours.Setup(p => p.GetDailyHoursAsync(Person, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null);
        SetupEntries(Entry(WorkActivity, new DateOnly(2026, 8, 3), 8, 16));

        var result = await CreateSut().ComputeMonthAsync(2026, 8, new[] { Employee() }, CancellationToken.None);

        var row = result.Single();
        row.DailyContractHours.Should().BeNull();
        row.RequiredHours.Should().Be(0m);
        row.Warnings.Should().Contain(w => w.Contains("úvazek"));
    }

    [Fact]
    public async Task HourlessAndInProgressEntries_ProduceWarnings()
    {
        SetupEntries(
            new LogetoTimeEntry { Guid = Guid.NewGuid(), Person = Person, Date = new DateOnly(2026, 8, 3), Activity = VacationActivity }, // no hours at all
            new LogetoTimeEntry
            {
                Guid = Guid.NewGuid(),
                Person = Person,
                Date = new DateOnly(2026, 8, 4),
                Activity = WorkActivity,
                From = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero)   // in progress
            });

        var result = await CreateSut().ComputeMonthAsync(2026, 8, new[] { Employee() }, CancellationToken.None);

        result.Single().Warnings.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task UnmappedNonWorkActivity_GoesToOther_NotCredited_WithWarning()
    {
        var unknown = Guid.NewGuid();
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work },
                new() { Guid = unknown, Name = "Školení???", Type = "Absence" }
            });
        SetupEntries(HoursEntry(unknown, new DateOnly(2026, 8, 3), "04:00:00"));

        var result = await CreateSut().ComputeMonthAsync(2026, 8, new[] { Employee() }, CancellationToken.None);

        var row = result.Single();
        row.OtherAbsenceHours.Should().Be(4.00m);
        row.DeltaHours.Should().Be(-134.40m);   // not credited
        row.Warnings.Should().Contain(w => w.Contains("Školení???"));
    }
}
