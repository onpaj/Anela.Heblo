using Anela.Heblo.Application.Features.Manufacture.Model;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class TimePeriodCalculator : ITimePeriodCalculator
{
    public (DateTime fromDate, DateTime toDate) CalculateTimePeriod(
        TimePeriodFilter timePeriod,
        DateTime? customFromDate = null,
        DateTime? customToDate = null)
    {
        var now = DateTime.UtcNow;

        switch (timePeriod)
        {
            case TimePeriodFilter.PreviousQuarter:
                // Last 3 completed months
                var startOfCurrentMonth = new DateTime(now.Year, now.Month, 1);
                var endOfPreviousMonth = startOfCurrentMonth.AddDays(-1);
                var startOfPreviousQuarter = startOfCurrentMonth.AddMonths(-3);
                return (startOfPreviousQuarter, endOfPreviousMonth);

            case TimePeriodFilter.FutureQuarter:
                // Next 3 months from previous year (for demand forecasting)
                var startOfFutureQuarterLastYear = new DateTime(now.Year - 1, now.Month, 1);
                var endOfFutureQuarterLastYear = startOfFutureQuarterLastYear.AddMonths(3).AddDays(-1);
                return (startOfFutureQuarterLastYear, endOfFutureQuarterLastYear);

            case TimePeriodFilter.Y2Y:
                // Last 12 months
                var startOfY2Y = new DateTime(now.Year, now.Month, 1).AddMonths(-12);
                var endOfY2Y = new DateTime(now.Year, now.Month, 1).AddDays(-1);
                return (startOfY2Y, endOfY2Y);

            case TimePeriodFilter.PreviousSeason:
                // October-January of previous year
                var seasonStart = new DateTime(now.Year - 1, 10, 1);
                var seasonEnd = new DateTime(now.Year, 1, 31);
                return (seasonStart, seasonEnd);

            case TimePeriodFilter.CustomPeriod:
                if (customFromDate.HasValue && customToDate.HasValue)
                {
                    return (customFromDate.Value, customToDate.Value);
                }
                goto default; // Fall back to default if custom dates not provided

            default:
                // Default to previous quarter
                var defaultStart = new DateTime(now.Year, now.Month, 1).AddMonths(-3);
                var defaultEnd = new DateTime(now.Year, now.Month, 1).AddDays(-1);
                return (defaultStart, defaultEnd);
        }
    }
}