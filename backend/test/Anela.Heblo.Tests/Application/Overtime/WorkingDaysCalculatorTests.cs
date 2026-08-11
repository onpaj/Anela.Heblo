using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using FluentAssertions;

namespace Anela.Heblo.Tests.Application.Overtime;

public class WorkingDaysCalculatorTests
{
    [Theory]
    [InlineData(2026, 1, 1)]   // Nový rok
    [InlineData(2026, 4, 3)]   // Velký pátek (Easter 2026-04-05)
    [InlineData(2026, 4, 6)]   // Velikonoční pondělí
    [InlineData(2026, 5, 1)]   // Svátek práce
    [InlineData(2026, 5, 8)]   // Den vítězství
    [InlineData(2026, 7, 5)]   // Cyril a Metoděj
    [InlineData(2026, 7, 6)]   // Jan Hus
    [InlineData(2026, 9, 28)]  // Den české státnosti
    [InlineData(2026, 10, 28)] // Vznik ČSR
    [InlineData(2026, 11, 17)] // Den boje za svobodu
    [InlineData(2026, 12, 24)]
    [InlineData(2026, 12, 25)]
    [InlineData(2026, 12, 26)]
    public void IsPublicHoliday_ReturnsTrue_ForCzechHolidays(int y, int m, int d)
        => CzechHolidays.IsPublicHoliday(new DateOnly(y, m, d)).Should().BeTrue();

    [Fact]
    public void IsPublicHoliday_ReturnsFalse_ForOrdinaryDay()
        => CzechHolidays.IsPublicHoliday(new DateOnly(2026, 8, 11)).Should().BeFalse();

    [Fact]
    public void CountWorkingDays_July2026_Is22()
    {
        // July 2026: 23 weekdays; 6.7. (Monday, Jan Hus) is a holiday; 5.7. falls on Sunday → 22.
        WorkingDaysCalculator.CountWorkingDays(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31))
            .Should().Be(22);
    }

    [Fact]
    public void CountWorkingDays_August2026_Is21()
    {
        // August 2026: no holidays, 21 weekdays.
        WorkingDaysCalculator.CountWorkingDays(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31))
            .Should().Be(21);
    }

    [Fact]
    public void CountWorkingDays_PartialRange_CountsFromStart()
    {
        // 2026-08-17 (Mon) .. 2026-08-31 (Mon) = 11 weekdays, no holidays.
        WorkingDaysCalculator.CountWorkingDays(new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 31))
            .Should().Be(11);
    }
}
