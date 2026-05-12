using Anela.Heblo.Application.Common.TimePeriods;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface ITimePeriodCalculator
{
    (DateTime fromDate, DateTime toDate) CalculateTimePeriod(
        TimePeriod timePeriod,
        DateTime? customFromDate = null,
        DateTime? customToDate = null);

    IReadOnlyList<(DateTime fromDate, DateTime toDate)> CalculateTimePeriodRanges(
        TimePeriod timePeriod,
        DateTime? customFromDate = null,
        DateTime? customToDate = null);
}
